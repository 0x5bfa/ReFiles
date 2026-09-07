// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities.Changes;
using System.Threading.Channels;

namespace Files.Core.Browsing;

internal sealed class BrowseChangeCoordinator : IAsyncDisposable
{
	private const int ChangeQueueCapacity = 256;

	private readonly Func<CancellationToken, ValueTask> _processPendingAsync;
	private readonly Channel<BrowseQueuedChange> _queue = Channel.CreateBounded<BrowseQueuedChange>(new BoundedChannelOptions(ChangeQueueCapacity)
	{
		FullMode = BoundedChannelFullMode.Wait,
		SingleReader = true,
		SingleWriter = false,
		AllowSynchronousContinuations = false,
	});
	private readonly Queue<BrowseQueuedChange> _deferred = [];
	private readonly SemaphoreSlim _signal = new(0, 1);
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Lock _disposalLock = new();
	private readonly Lock _fullRefreshLock = new();
	private readonly SortedSet<long> _requestedFullRefreshGenerations = [];
	private readonly Task _pumpTask;
	private Task? _disposeTask;
	private int _signalPending;

	internal CancellationToken LifetimeToken => _lifetime.Token;

	internal long RequestedFullRefreshGeneration
	{
		get
		{
			lock (_fullRefreshLock)
			{
				return _requestedFullRefreshGenerations.Count is 0 ? 0 : _requestedFullRefreshGenerations.Max;
			}
		}
	}

	internal BrowseChangeCoordinator(Func<CancellationToken, ValueTask> processPendingAsync)
	{
		ArgumentNullException.ThrowIfNull(processPendingAsync);

		_processPendingAsync = processPendingAsync;
		_pumpTask = PumpAsync(_lifetime.Token);
	}

	internal bool TryEnqueue(BrowseQueuedChange change)
	{
		if (!_queue.Writer.TryWrite(change))
		{
			return false;
		}

		Signal();

		return true;
	}

	internal bool RequestFullRefresh(long generation)
	{
		lock (_fullRefreshLock)
		{
			_requestedFullRefreshGenerations.Add(generation);
		}

		Signal();

		return true;
	}

	internal bool TryClearFullRefresh(long generation)
	{
		bool removed;
		lock (_fullRefreshLock)
		{
			removed = _requestedFullRefreshGenerations.Remove(generation);
		}

		if (removed)
		{
			Signal();
		}

		return removed;
	}

	internal bool TryRead(out BrowseQueuedChange change)
	{
		if (_deferred.Count is not 0)
		{
			change = _deferred.Dequeue();

			return true;
		}

		return _queue.Reader.TryRead(out change);
	}

	internal void Defer(BrowseQueuedChange change)
	{
		_deferred.Enqueue(change);
	}

	internal void Signal()
	{
		if (Interlocked.Exchange(ref _signalPending, 1) is not 0)
		{
			return;
		}

		try
		{
			_signal.Release();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			_disposeTask ??= DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	private async Task PumpAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
				Interlocked.Exchange(ref _signalPending, 0);

				try
				{
					await _processPendingAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch
				{
					// The session callback records refresh failures in its public state.
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private async Task DisposeCoreAsync()
	{
		_lifetime.Cancel();
		Signal();

		try
		{
			await _pumpTask.ConfigureAwait(false);
		}
		finally
		{
			_queue.Writer.TryComplete();
			_signal.Dispose();
			_lifetime.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}

internal readonly record struct BrowseQueuedChange(long Generation, FolderChange Change);
