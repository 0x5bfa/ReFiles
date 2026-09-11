// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Infrastructure;

internal sealed class SearchTextDebouncer : IDisposable
{
	private readonly TimeSpan _delay;
	private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
	private CancellationTokenSource? _pendingCancellation;
	private int _isDisposed;

	internal SearchTextDebouncer(TimeSpan delay, Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
	{
		if (delay < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(delay));
		}

		_delay = delay;
		_delayAsync = delayAsync ?? Task.Delay;
	}

	internal Task ScheduleAsync(string text, Func<string, CancellationToken, Task> action)
	{
		ArgumentNullException.ThrowIfNull(text);

		ArgumentNullException.ThrowIfNull(action);

		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			return Task.CompletedTask;
		}

		var cancellation = new CancellationTokenSource();
		var previousCancellation = Interlocked.Exchange(ref _pendingCancellation, cancellation);
		CancelCancellation(previousCancellation);
		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			Interlocked.CompareExchange(ref _pendingCancellation, null, cancellation);
			CancelCancellation(cancellation);
			cancellation.Dispose();

			return Task.CompletedTask;
		}

		return RunAsync(text, action, cancellation);
	}

	internal void Cancel()
	{
		var cancellation = Interlocked.Exchange(ref _pendingCancellation, null);
		CancelCancellation(cancellation);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		Cancel();
	}

	private async Task RunAsync(string text, Func<string, CancellationToken, Task> action, CancellationTokenSource cancellation)
	{
		try
		{
			await _delayAsync(_delay, cancellation.Token);
			if (cancellation.IsCancellationRequested || !ReferenceEquals(Volatile.Read(ref _pendingCancellation), cancellation))
			{
				return;
			}

			await action(text, cancellation.Token);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		finally
		{
			if (ReferenceEquals(Volatile.Read(ref _pendingCancellation), cancellation))
			{
				Interlocked.CompareExchange(ref _pendingCancellation, null, cancellation);
			}

			cancellation.Dispose();
		}
	}

	private static void CancelCancellation(CancellationTokenSource? cancellation)
	{
		try
		{
			cancellation?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
	}
}
