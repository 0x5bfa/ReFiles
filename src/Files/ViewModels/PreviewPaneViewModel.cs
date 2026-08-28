// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Core.Browsing;
using Files.Core.Capabilities.Previews;
using Files.Core.Sessions;
using Files.Infrastructure;
using Files.Localization;

namespace Files.ViewModels;

public sealed class PreviewPaneViewModel : ObservableObject, IDisposable
{
	private readonly IBrowsePreviewModel _preview;
	private readonly IUIDispatcher _dispatcher;

	private BrowsePreviewSnapshot _snapshot;
	private int _isDisposed;

	public BrowsePreviewSnapshot Snapshot => _snapshot;

	public BrowsePreviewStatus Status => _snapshot.Status;

	public PreviewResult? Result => _snapshot.Result;

	public StreamPreviewResult? StreamResult => _snapshot.Result as StreamPreviewResult;

	public WindowsShellPreviewResult? ShellResult => _snapshot.Result as WindowsShellPreviewResult;

	public bool IsLoading => Status is BrowsePreviewStatus.Loading;

	public bool HasContent => Status is BrowsePreviewStatus.Ready && Result is not null;

	public string StatusText => Status switch
	{
		BrowsePreviewStatus.Empty => Strings.PreviewEmpty.GetLocalized(),
		BrowsePreviewStatus.Loading => Strings.Loading.GetLocalized(),
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
		_preview.Changed += Preview_Changed;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_preview.Changed -= Preview_Changed;
	}

	private void Preview_Changed(object? sender, EventArgs e)
	{
		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		if (_dispatcher.HasThreadAccess)
		{
			ApplySnapshot(_preview.Current);

			return;
		}

		if (!_dispatcher.TryEnqueue(() => ApplySnapshot(_preview.Current)))
		{
			return;
		}
	}

	private void ApplySnapshot(BrowsePreviewSnapshot snapshot)
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || snapshot.RequestVersion < _snapshot.RequestVersion)
		{
			return;
		}

		_snapshot = snapshot;
		OnPropertyChanged(nameof(Snapshot));
		OnPropertyChanged(nameof(Status));
		OnPropertyChanged(nameof(Result));
		OnPropertyChanged(nameof(StreamResult));
		OnPropertyChanged(nameof(ShellResult));
		OnPropertyChanged(nameof(IsLoading));
		OnPropertyChanged(nameof(HasContent));
		OnPropertyChanged(nameof(StatusText));
	}

	private static IBrowsePreviewModel GetPreviewModel(BrowsePaneSession pane)
	{
		ArgumentNullException.ThrowIfNull(pane);

		return pane.Preview;
	}
}
