// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Previews;
using Files.Core.Models;

namespace Files.Core.Browsing;

/// <summary>
/// Loads and owns the preview for the current single selection in a browse session.
/// </summary>
public sealed class BrowsePreviewModel : IBrowsePreviewModel
{
	private static readonly TimeSpan DefaultRefreshDelay =
		TimeSpan.FromMilliseconds(100);

	private readonly IBrowseSessionModel browseSession;
	private readonly TimeSpan refreshDelay;
	private readonly object stateLock = new();
	private readonly CancellationTokenSource lifetime = new();
	private BrowsePreviewSnapshot current = new(0, null, BrowsePreviewStatus.Empty);
	private CancellationTokenSource? activeRequestCts;
	private Task? activeRequestTask;
	private Task? disposeTask;
	private long currentRequestVersion;
	private bool isDisposed;

	public BrowsePreviewModel(IBrowseSessionModel browseSession, TimeSpan? refreshDelay = null)
	{
		ArgumentNullException.ThrowIfNull(browseSession);

		this.browseSession = browseSession;
		this.refreshDelay = refreshDelay ?? DefaultRefreshDelay;
		if (this.refreshDelay < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(refreshDelay));
		}

		browseSession.SelectionChanged += OnSelectionChanged;
		browseSession.ItemsChanged += OnItemsChanged;
	}

	public BrowsePreviewSnapshot Current
	{
		get
		{
			lock (stateLock)
			{
				return current;
			}
		}
	}

	public event EventHandler? Changed;

	public ValueTask RefreshAsync(
		PreviewHydrationPolicy hydrationPolicy = PreviewHydrationPolicy.LocalOnly,
		CancellationToken cancellationToken = default)
	{
		if (hydrationPolicy is not PreviewHydrationPolicy.LocalOnly
			and not PreviewHydrationPolicy.AllowHydration)
		{
			throw new ArgumentOutOfRangeException(nameof(hydrationPolicy));
		}

		return new ValueTask(BeginRefresh(hydrationPolicy, cancellationToken));
	}

	public ValueTask DisposeAsync()
	{
		CancellationTokenSource? requestCts;
		Task? requestTask;
		TaskCompletionSource<object?> completion;

		lock (stateLock)
		{
			if (disposeTask is not null)
			{
				return new ValueTask(disposeTask);
			}

			isDisposed = true;
			Interlocked.Increment(ref currentRequestVersion);
			browseSession.SelectionChanged -= OnSelectionChanged;
			browseSession.ItemsChanged -= OnItemsChanged;

			requestCts = activeRequestCts;
			requestTask = activeRequestTask;
			activeRequestCts = null;
			activeRequestTask = null;
			completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
			disposeTask = completion.Task;
		}

		requestCts?.Cancel();
		_ = DisposeCoreAsync(requestCts, requestTask, completion);
		return new ValueTask(completion.Task);
	}

	private Task BeginRefresh(PreviewHydrationPolicy hydrationPolicy, CancellationToken cancellationToken)
	{
		var requestCts = cancellationToken.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken)
			: CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
		CancellationTokenSource? previousCts;
		long requestVersion;

		lock (stateLock)
		{
			if (isDisposed)
			{
				requestCts.Dispose();
				throw new ObjectDisposedException(nameof(BrowsePreviewModel));
			}
			requestVersion = Interlocked.Increment(ref currentRequestVersion);
			previousCts = activeRequestCts;
			activeRequestCts = requestCts;
			activeRequestTask = null;
		}

		previousCts?.Cancel();
		var requestTask = LoadAsync(requestVersion, hydrationPolicy, requestCts);

		lock (stateLock)
		{
			if (ReferenceEquals(activeRequestCts, requestCts))
			{
				activeRequestTask = requestTask;
			}
		}

		return requestTask;
	}

	private async Task LoadAsync(long requestVersion, PreviewHydrationPolicy hydrationPolicy, CancellationTokenSource requestCts)
	{
		try
		{
			await Task.Yield();
			await Task.Delay(refreshDelay, requestCts.Token).ConfigureAwait(false);

			var target = ResolveSelectedItem();
			if (target is null)
			{
				await PublishAsync(new BrowsePreviewSnapshot(requestVersion, null, BrowsePreviewStatus.Empty)).ConfigureAwait(false);
				return;
			}

			var key = target.Reference.GetKey();
			var generation = browseSession.Generation;
			await PublishAsync(new BrowsePreviewSnapshot(requestVersion, key, BrowsePreviewStatus.Loading)).ConfigureAwait(false);

			var source = target.Get<IPreviewSource>();
			if (source is null)
			{
				await PublishAsync(new BrowsePreviewSnapshot(requestVersion, key, BrowsePreviewStatus.Unavailable)).ConfigureAwait(false);
				return;
			}

			var result = await source
				.GetPreviewAsync(new PreviewRequest(maximumBytes: 32 * 1024 * 1024, hydrationPolicy), requestCts.Token)
				.ConfigureAwait(false);

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
			if (requestVersion == Volatile.Read(ref currentRequestVersion))
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
		var selection = browseSession.Selection;
		if (selection.SelectedKeys.Count is not 1)
		{
			return null;
		}

		var selectedKey = selection.SelectedKeys[0];
		return browseSession.Items.FirstOrDefault(item => item.Reference.GetKey() == selectedKey);
	}

	private bool IsStillCurrent(long requestVersion, long generation, StorableKey key, IStorableModel originalModel)
	{
		if (requestVersion != Volatile.Read(ref currentRequestVersion)
			|| browseSession.Generation != generation)
		{
			return false;
		}

		var selection = browseSession.Selection;
		if (selection.SelectedKeys.Count is not 1
			|| selection.SelectedKeys[0] != key)
		{
			return false;
		}

		var currentModel = browseSession.Items.FirstOrDefault(item => item.Reference.GetKey() == key);
		return ReferenceEquals(currentModel, originalModel);
	}

	private async ValueTask<bool> PublishAsync(BrowsePreviewSnapshot next)
	{
		PreviewResult? previousResult = null;
		var accepted = false;

		lock (stateLock)
		{
			if (!isDisposed
				&& next.RequestVersion == Volatile.Read(ref currentRequestVersion))
			{
				previousResult = current.Result;
				current = next;
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

		if (previousResult is not null
			&& !ReferenceEquals(previousResult, next.Result))
		{
			await previousResult.DisposeAsync().ConfigureAwait(false);
		}

		RaiseChanged();
		return true;
	}

	private void CompleteRequest(CancellationTokenSource requestCts)
	{
		lock (stateLock)
		{
			if (ReferenceEquals(activeRequestCts, requestCts))
			{
				activeRequestCts = null;
				activeRequestTask = null;
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
			lock (stateLock)
			{
				result = current.Result;
				current = new BrowsePreviewSnapshot(Volatile.Read(ref currentRequestVersion), null, BrowsePreviewStatus.Empty);
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
			lifetime.Dispose();
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
