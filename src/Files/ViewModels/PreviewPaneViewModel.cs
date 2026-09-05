// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.Core.Browsing;
using Files.Core.Capabilities.Previews;
using Files.Core.Sessions;
using Files.Infrastructure;
using Files.Localization;
using Files.Core.Windows;

namespace Files.ViewModels;

public sealed class PreviewPaneViewModel : ObservableObject, IDisposable
{
	private readonly IBrowsePreviewModel _preview;
	private readonly IUIDispatcher _dispatcher;
	private readonly ConcurrentDictionary<StreamPreviewContentLease, byte> _pendingStreamRetentions = [];

	private BrowsePreviewSnapshot _snapshot;
	private StreamPreviewContentLease? _streamRetention;
	private int _isDisposed;

	public BrowsePreviewSnapshot Snapshot => _snapshot;

	public BrowsePreviewStatus Status => _snapshot.Status;

	public PreviewResult? Result => _snapshot.Result;

	public StreamPreviewResult? StreamResult => _snapshot.Result as StreamPreviewResult;

	public WindowsShellPreviewResult? ShellResult => _snapshot.Result as WindowsShellPreviewResult;

	public bool IsLoading => Status is BrowsePreviewStatus.Loading;

	public bool HasContent => Status is BrowsePreviewStatus.Ready && Result is not null;

	public bool CanPreviewUntrusted => Status is BrowsePreviewStatus.Blocked && _snapshot.BlockReason is PreviewBlockReason.Untrusted;

	public string StatusText => Status switch
	{
		BrowsePreviewStatus.Empty => Strings.PreviewEmpty.GetLocalized(),
		BrowsePreviewStatus.Loading => Strings.Loading.GetLocalized(),
		BrowsePreviewStatus.Blocked when _snapshot.BlockReason is PreviewBlockReason.Untrusted => Strings.PreviewUntrusted.GetLocalized(),
		BrowsePreviewStatus.Blocked => Strings.PreviewBlocked.GetLocalized(),
		BrowsePreviewStatus.Unavailable => Strings.PreviewUnavailable.GetLocalized(),
		BrowsePreviewStatus.Failed => Strings.PreviewFailed.GetLocalized(),
		_ => string.Empty,
	};

	public PreviewPaneViewModel(BrowsePaneSession pane, IUIDispatcher dispatcher)
		: this(GetPreviewModel(pane), dispatcher)
	{
	}

	internal PreviewPaneViewModel(IBrowsePreviewModel preview, IUIDispatcher dispatcher)
	{
		ArgumentNullException.ThrowIfNull(preview);
		ArgumentNullException.ThrowIfNull(dispatcher);

		_preview = preview;
		_dispatcher = dispatcher;
		_snapshot = preview.Current;
		_streamRetention = RetainStream(_snapshot);
		_preview.Changed += Preview_Changed;
	}

	public ValueTask PreviewUntrustedAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

		return _preview.PreviewUntrustedAsync(_snapshot, cancellationToken);
	}

	public bool TryReportShellPreviewBlocked(BrowsePreviewSnapshot expectedSnapshot, PreviewBlockReason reason)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

		return _preview.TryReportShellPreviewBlocked(expectedSnapshot, reason);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_preview.Changed -= Preview_Changed;
		Interlocked.Exchange(ref _streamRetention, null)?.Dispose();
		foreach (var retention in _pendingStreamRetentions.Keys)
		{
			if (_pendingStreamRetentions.TryRemove(retention, out _))
			{
				retention.Dispose();
			}
		}
	}

	private void Preview_Changed(object? sender, EventArgs e)
	{
		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		var snapshot = _preview.Current;
		var streamRetention = RetainStream(snapshot);
		if (_dispatcher.HasThreadAccess)
		{
			ApplySnapshot(snapshot, streamRetention);

			return;
		}

		if (streamRetention is not null)
		{
			_pendingStreamRetentions.TryAdd(streamRetention, 0);
			if (Volatile.Read(ref _isDisposed) is not 0 && _pendingStreamRetentions.TryRemove(streamRetention, out _))
			{
				streamRetention.Dispose();

				return;
			}
		}

		if (!_dispatcher.TryEnqueue(() => ApplySnapshotFromDispatcher(snapshot, streamRetention)))
		{
			if (streamRetention is not null && _pendingStreamRetentions.TryRemove(streamRetention, out _))
			{
				streamRetention.Dispose();
			}

			return;
		}
	}

	private void ApplySnapshotFromDispatcher(BrowsePreviewSnapshot snapshot, StreamPreviewContentLease? streamRetention)
	{
		if (streamRetention is not null)
		{
			_pendingStreamRetentions.TryRemove(streamRetention, out _);
		}

		ApplySnapshot(snapshot, streamRetention);
	}

	private void ApplySnapshot(BrowsePreviewSnapshot snapshot, StreamPreviewContentLease? streamRetention)
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || snapshot.RequestVersion < _snapshot.RequestVersion)
		{
			streamRetention?.Dispose();

			return;
		}

		var previousStreamRetention = Interlocked.Exchange(ref _streamRetention, streamRetention);
		_snapshot = snapshot;
		OnPropertyChanged(nameof(Snapshot));
		OnPropertyChanged(nameof(Status));
		OnPropertyChanged(nameof(Result));
		OnPropertyChanged(nameof(StreamResult));
		OnPropertyChanged(nameof(ShellResult));
		OnPropertyChanged(nameof(IsLoading));
		OnPropertyChanged(nameof(HasContent));
		OnPropertyChanged(nameof(CanPreviewUntrusted));
		OnPropertyChanged(nameof(StatusText));
		previousStreamRetention?.Dispose();
	}

	private static StreamPreviewContentLease? RetainStream(BrowsePreviewSnapshot snapshot)
	{
		return snapshot.Result is StreamPreviewResult streamResult ? streamResult.AcquireContent() : null;
	}

	private static IBrowsePreviewModel GetPreviewModel(BrowsePaneSession pane)
	{
		ArgumentNullException.ThrowIfNull(pane);

		return pane.Preview;
	}
}
