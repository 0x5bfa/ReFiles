// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Files.BackgroundServer;

internal sealed class ServerLifetime : IDisposable
{
	private readonly object _syncRoot = new();
	private readonly TimeSpan _idleTimeout;
	private readonly Timer _idleTimer;
	private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private long _idleStarted;
	private int _activityCount;
	private int _clientCount;
	private long _clientGeneration;
	private bool _stopping;

	internal Task ExitTask => _exit.Task;
	internal bool HasClients => Volatile.Read(ref _clientCount) != 0;
	internal long ClientGeneration => Interlocked.Read(ref _clientGeneration);

	internal ServerLifetime(TimeSpan idleTimeout)
	{
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleTimeout, TimeSpan.Zero);

		_idleTimeout = idleTimeout;
		_idleTimer = new Timer(OnIdleTimer);
		_idleStarted = Stopwatch.GetTimestamp();
		_idleTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
	}

	internal IDisposable Acquire(bool isClient = false)
	{
		lock (_syncRoot)
		{
			ObjectDisposedException.ThrowIf(_stopping, this);

			var lease = new ServerActivityLease(this, isClient);
			_activityCount++;
			if (isClient)
			{
				Interlocked.Increment(ref _clientCount);
				Interlocked.Increment(ref _clientGeneration);
			}
			_idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

			return lease;
		}
	}

	public void Dispose()
	{
		lock (_syncRoot)
		{
			_stopping = true;
			_idleTimer.Dispose();
			_exit.TrySetResult();
		}
	}

	internal void Release(bool isClient)
	{
		lock (_syncRoot)
		{
			_activityCount--;
			if (isClient)
			{
				Interlocked.Decrement(ref _clientCount);
			}
			if (_activityCount == 0 && !_stopping)
			{
				_idleStarted = Stopwatch.GetTimestamp();
				_idleTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
			}
		}
	}

	private void OnIdleTimer(object? state)
	{
		lock (_syncRoot)
		{
			if (_stopping || _activityCount != 0)
			{
				return;
			}

			var remaining = _idleTimeout - Stopwatch.GetElapsedTime(_idleStarted);
			if (remaining > TimeSpan.Zero)
			{
				_idleTimer.Change(remaining, Timeout.InfiniteTimeSpan);

				return;
			}

			_stopping = true;
			_exit.TrySetResult();
		}
	}

}
