// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;
using Files.Core.Models;

namespace Files.Core.Browsing;

/// <summary>
/// Loads and owns the preview for the current single selection in a browse session.
/// </summary>
public sealed class BrowsePreviewModel : IBrowsePreviewModel
{
	private static readonly TimeSpan _defaultRefreshDelay = TimeSpan.FromMilliseconds(100);

	private readonly IBrowseSession _browseSession;

	private readonly TimeSpan _refreshDelay;

	private readonly Lock _stateLock = new();

	private readonly CancellationTokenSource _lifetime = new();

	private BrowsePreviewSnapshot _current = new(0, null, BrowsePreviewStatus.Empty);

	private CancellationTokenSource? _activeRequestCts;

	private Task? _activeRequestTask;

	private Task? _disposeTask;

	private long _currentRequestVersion;

	private bool _isDisposed;

	/// <inheritdoc />
	public BrowsePreviewSnapshot Current
	{
		get
		{
			lock (_stateLock)
			{
				return _current;
			}
		}
	}

	/// <inheritdoc />
	public event EventHandler? Changed;

	/// <summary>Initializes a preview model for a browse session.</summary>
	/// <param name="browseSession">The browse session to observe.</param>
	/// <param name="refreshDelay">The optional selection debounce delay.</param>
	public BrowsePreviewModel(IBrowseSession browseSession, TimeSpan? refreshDelay = null)
	{
		ArgumentNullException.ThrowIfNull(browseSession);

		_browseSession = browseSession;
		_refreshDelay = refreshDelay ?? _defaultRefreshDelay;
		if (_refreshDelay < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(refreshDelay));
		}

		browseSession.SelectionChanged += OnSelectionChanged;
		browseSession.ItemsChanged += OnItemsChanged;
	}

	/// <inheritdoc />
	public ValueTask RefreshAsync(PreviewHydrationPolicy hydrationPolicy = PreviewHydrationPolicy.LocalOnly, CancellationToken cancellationToken = default)
	{
		if (hydrationPolicy is not PreviewHydrationPolicy.LocalOnly and not PreviewHydrationPolicy.AllowHydration)
		{
			throw new ArgumentOutOfRangeException(nameof(hydrationPolicy));
		}

		return new ValueTask(BeginRefresh(hydrationPolicy, cancellationToken));
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		CancellationTokenSource? requestCts;
		Task? requestTask;
		TaskCompletionSource<object?> completion;

		lock (_stateLock)
		{
			if (_disposeTask is not null)
			{
				return new ValueTask(_disposeTask);
			}

			_isDisposed = true;
			Interlocked.Increment(ref _currentRequestVersion);
			_browseSession.SelectionChanged -= OnSelectionChanged;
			_browseSession.ItemsChanged -= OnItemsChanged;

			requestCts = _activeRequestCts;
			requestTask = _activeRequestTask;
			_activeRequestCts = null;
			_activeRequestTask = null;
			completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
			_disposeTask = completion.Task;
		}

		requestCts?.Cancel();
		_ = DisposeCoreAsync(requestCts, requestTask, completion);

		return new ValueTask(completion.Task);
	}

	private Task BeginRefresh(PreviewHydrationPolicy hydrationPolicy, CancellationToken cancellationToken)
	{
		var requestCts = cancellationToken.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken)
			: CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		CancellationTokenSource? previousCts;
		long requestVersion;

		lock (_stateLock)
		{
			if (_isDisposed)
			{
				requestCts.Dispose();
				throw new ObjectDisposedException(nameof(BrowsePreviewModel));
			}
			requestVersion = Interlocked.Increment(ref _currentRequestVersion);
			previousCts = _activeRequestCts;
			_activeRequestCts = requestCts;
			_activeRequestTask = null;
		}

		previousCts?.Cancel();
		var requestTask = LoadAsync(requestVersion, hydrationPolicy, requestCts);

		lock (_stateLock)
		{
			if (ReferenceEquals(_activeRequestCts, requestCts))
			{
				_activeRequestTask = requestTask;
			}
		}

		return requestTask;
	}

	private async Task LoadAsync(long requestVersion, PreviewHydrationPolicy hydrationPolicy, CancellationTokenSource requestCts)
	{
		try
		{
			await Task.Yield();
			await Task.Delay(_refreshDelay, requestCts.Token).ConfigureAwait(false);

			var target = ResolveSelectedItem();
			if (target is null)
			{
				await PublishAsync(new BrowsePreviewSnapshot(requestVersion, null, BrowsePreviewStatus.Empty)).ConfigureAwait(false);

				return;
			}

			var key = target.Reference.GetKey();
			var generation = _browseSession.Generation;
			await PublishAsync(new BrowsePreviewSnapshot(requestVersion, key, BrowsePreviewStatus.Loading)).ConfigureAwait(false);

			var source = target.Get<IPreviewSource>();
			if (source is null)
			{
				await PublishAsync(new BrowsePreviewSnapshot(requestVersion, key, BrowsePreviewStatus.Unavailable)).ConfigureAwait(false);

				return;
			}

			var result = await source.GetPreviewAsync(new PreviewRequest(maximumBytes: 32 * 1024 * 1024, hydrationPolicy), requestCts.Token).ConfigureAwait(false);

			if (!IsStillCurrent(requestVersion, generation, key, target))
			{
				if (result is not null)
				{
					await result.DisposeAsync().ConfigureAwait(false);
				}

				return;
			}

			var status = result switch
			{
				null => BrowsePreviewStatus.Unavailable,
				BlockedPreviewResult => BrowsePreviewStatus.Blocked,
				_ => BrowsePreviewStatus.Ready,
			};
			PreviewBlockReason? blockReason = result is BlockedPreviewResult blocked
				? blocked.Reason
				: null;

			await PublishAsync(new BrowsePreviewSnapshot(requestVersion, key, status, result, blockReason)).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
			when (requestCts.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			if (requestVersion == Volatile.Read(ref _currentRequestVersion))
			{
				await PublishAsync(new BrowsePreviewSnapshot(requestVersion, null, BrowsePreviewStatus.Failed, Error: exception)).ConfigureAwait(false);
			}
		}
		finally
		{
			CompleteRequest(requestCts);
		}
	}

	private IStorableModel? ResolveSelectedItem()
	{
		var selection = _browseSession.Selection;
		if (selection.SelectedKeys.Count is not 1)
		{
			return null;
		}

		var selectedKey = selection.SelectedKeys[0];

		return _browseSession.Items.FirstOrDefault(item => item.Reference.GetKey() == selectedKey);
	}

	private bool IsStillCurrent(long requestVersion, long generation, StorableKey key, IStorableModel originalModel)
	{
		if (requestVersion != Volatile.Read(ref _currentRequestVersion) || _browseSession.Generation != generation)
		{
			return false;
		}

		var selection = _browseSession.Selection;
		if (selection.SelectedKeys.Count is not 1 || selection.SelectedKeys[0] != key)
		{
			return false;
		}

		var currentModel = _browseSession.Items.FirstOrDefault(item => item.Reference.GetKey() == key);

		return ReferenceEquals(currentModel, originalModel);
	}

	private async ValueTask<bool> PublishAsync(BrowsePreviewSnapshot next)
	{
		PreviewResult? previousResult = null;
		var accepted = false;

		lock (_stateLock)
		{
			if (!_isDisposed && next.RequestVersion == Volatile.Read(ref _currentRequestVersion))
			{
				previousResult = _current.Result;
				_current = next;
				accepted = true;
			}
		}

		if (!accepted)
		{
			if (next.Result is not null)
			{
				await next.Result.DisposeAsync().ConfigureAwait(false);
			}

			return false;
		}

		if (previousResult is not null && !ReferenceEquals(previousResult, next.Result))
		{
			await previousResult.DisposeAsync().ConfigureAwait(false);
		}

		RaiseChanged();

		return true;
	}

	private void CompleteRequest(CancellationTokenSource requestCts)
	{
		lock (_stateLock)
		{
			if (ReferenceEquals(_activeRequestCts, requestCts))
			{
				_activeRequestCts = null;
				_activeRequestTask = null;
			}
		}

		requestCts.Dispose();
	}

	private async Task DisposeCoreAsync(CancellationTokenSource? requestCts, Task? requestTask, TaskCompletionSource<object?> completion)
	{
		try
		{
			try
			{
				if (requestTask is not null)
				{
					await requestTask.ConfigureAwait(false);
				}
			}
			catch (Exception exception)
			{
				Trace.TraceError("BrowsePreviewModel request failed during disposal: {0}", exception);
			}

			PreviewResult? result;
			lock (_stateLock)
			{
				result = _current.Result;
				_current = new BrowsePreviewSnapshot(Volatile.Read(ref _currentRequestVersion), null, BrowsePreviewStatus.Empty);
			}

			if (result is not null)
			{
				await result.DisposeAsync().ConfigureAwait(false);
			}

			completion.TrySetResult(null);
		}
		catch (Exception exception)
		{
			completion.TrySetException(exception);
		}
		finally
		{
			requestCts?.Dispose();
			_lifetime.Dispose();
		}
	}

	private void OnSelectionChanged(object? sender, EventArgs args)
	{
		RequestRefresh();
	}

	private void OnItemsChanged(object? sender, BrowseItemsChangedEventArgs args)
	{
		RequestRefresh();
	}

	private void RequestRefresh()
	{
		try
		{
			_ = BeginRefresh(PreviewHydrationPolicy.LocalOnly, CancellationToken.None);
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private void RaiseChanged()
	{
		var handlers = Changed;
		if (handlers is null)
		{
			return;
		}

		foreach (EventHandler handler in handlers.GetInvocationList())
		{
			try
			{
				handler(this, EventArgs.Empty);
			}
			catch (Exception exception)
			{
				Trace.TraceError("BrowsePreviewModel event handler failed: {0}", exception);
			}
		}
	}
}
