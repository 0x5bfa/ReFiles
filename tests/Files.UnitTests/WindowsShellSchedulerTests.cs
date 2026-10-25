// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Windows;
using Windows.Win32.UI.WindowsAndMessaging;
using NativeMethods = Windows.Win32.TestNativeMethods;

namespace Files.UnitTests;

/// <summary>Tests Windows Shell scheduler ordering, cancellation, message pumping, and lifetime.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsShellSchedulerTests
{
	private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(10);

	/// <summary>Verifies queued work runs in FIFO order on the ordered STA.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task OrderedLaneRunsOnStaAndPreservesFifoOrder()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		var order = new ConcurrentQueue<int>();
		var tasks = Enumerable.Range(0, 16).Select(index => scheduler.InvokeAsync(() =>
		{
			Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
			order.Enqueue(index);

			return index;
		})).ToArray();

		await Task.WhenAll(tasks).WaitAsync(_testTimeout);
		CollectionAssert.AreEqual(Enumerable.Range(0, 16).ToArray(), order.ToArray());
	}

	/// <summary>Verifies the concurrent lane respects its configured worker count.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task ConcurrentLaneDoesNotExceedConfiguredWorkerCount()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		using var release = new ManualResetEventSlim();
		var active = 0;
		var maximumActive = 0;
		var tasks = Enumerable.Range(0, 8).Select(_ => scheduler.InvokeConcurrentAsync(() =>
		{
			var current = Interlocked.Increment(ref active);
			SpinUpdateMaximum(ref maximumActive, current);

			try
			{
				WaitForRelease(release);

				return current;
			}
			finally
			{
				Interlocked.Decrement(ref active);
			}
		})).ToArray();

		try
		{
			Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref maximumActive) == 2, _testTimeout), "Concurrent workers did not reach the expected parallelism.");
		}
		finally
		{
			release.Set();
		}

		await Task.WhenAll(tasks).WaitAsync(_testTimeout);
		Assert.AreEqual(2, maximumActive);
	}

	/// <summary>Verifies each concurrent delegate stays on one STA.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task ConcurrentDelegateRunsEntirelyOnOneSta()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		var threadIds = new ConcurrentBag<int>();
		var apartmentStates = new ConcurrentBag<ApartmentState>();

		await scheduler.InvokeConcurrentAsync(() =>
		{
			for (var index = 0; index < 16; index++)
			{
				threadIds.Add(Environment.CurrentManagedThreadId);
				apartmentStates.Add(Thread.CurrentThread.GetApartmentState());
				Thread.Yield();
			}

			return true;
		}).WaitAsync(_testTimeout);

		Assert.AreEqual(1, threadIds.Distinct().Count());
		Assert.AreEqual(1, apartmentStates.Distinct().Count());
		Assert.AreEqual(ApartmentState.STA, apartmentStates.Distinct().Single());
	}

	/// <summary>Verifies blocked search work does not occupy a concurrent worker.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task BlockedSearchDoesNotOccupyConcurrentLane()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		var search = scheduler.InvokeSearchAsync(() =>
		{
			started.SetResult(Environment.CurrentManagedThreadId);
			WaitForRelease(release);

			return true;
		});

		try
		{
			var searchThread = await started.Task.WaitAsync(_testTimeout);
			var concurrentThread = await scheduler.InvokeConcurrentAsync(() => Environment.CurrentManagedThreadId).WaitAsync(_testTimeout);
			Assert.AreNotEqual(searchThread, concurrentThread);
			Assert.IsFalse(search.IsCompleted);
		}
		finally
		{
			release.Set();
		}

		Assert.IsTrue(await search.WaitAsync(_testTimeout));
	}

	/// <summary>Verifies nested ordered work runs immediately on the current STA.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task NestedOrderedInvocationRunsWithoutDeadlock()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		await scheduler.InvokeAsync(() =>
		{
			var thread = Environment.CurrentManagedThreadId;
			var nested = scheduler.InvokeAsync(() => (Thread.CurrentThread.GetApartmentState(), Environment.CurrentManagedThreadId));
			var result = nested.WaitAsync(_testTimeout).GetAwaiter().GetResult();
			Assert.AreEqual(ApartmentState.STA, result.Item1);
			Assert.AreEqual(thread, result.Item2);

			return true;
		}).WaitAsync(_testTimeout);
	}

	/// <summary>Verifies nested work runs ahead of work already in the queue.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task NestedInvocationRunsAheadOfQueuedWork()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var order = new ConcurrentQueue<int>();
		var active = scheduler.InvokeAsync(() =>
		{
			order.Enqueue(1);
			started.SetResult(true);
			WaitForRelease(release);
			scheduler.InvokeAsync(() =>
			{
				order.Enqueue(2);

				return true;
			}).WaitAsync(_testTimeout).GetAwaiter().GetResult();
			order.Enqueue(3);

			return true;
		});
		Task<bool> queued;

		try
		{
			await started.Task.WaitAsync(_testTimeout);
			queued = scheduler.InvokeAsync(() =>
			{
				order.Enqueue(4);

				return true;
			});
		}
		finally
		{
			release.Set();
		}

		await Task.WhenAll(active, queued).WaitAsync(_testTimeout);
		CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, order.ToArray());
	}

	/// <summary>Verifies disposal rejects nested calls on every lane.</summary>
	/// <param name="lane">The lane to invoke.</param>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	[DataRow("ordered")]
	[DataRow("concurrent")]
	[DataRow("search")]
	[DataRow("operation")]
	public async Task DisposalRejectsNestedInvocation(string lane)
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		var executed = false;
		Task? disposal = null;

		await InvokeOnLane(scheduler, lane, () =>
		{
			disposal = scheduler.DisposeAsync().AsTask();
			var nested = InvokeOnLane(scheduler, lane, () =>
			{
				executed = true;

				return true;
			});
			Assert.IsTrue(nested.IsFaulted, "A nested call was accepted after disposal started.");
			Assert.IsInstanceOfType<ObjectDisposedException>(nested.Exception!.InnerException);

			return true;
		}).WaitAsync(_testTimeout);

		Assert.IsNotNull(disposal);
		await disposal.WaitAsync(_testTimeout);
		Assert.IsFalse(executed);
	}

	/// <summary>Verifies a token canceled before submission prevents execution.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task CancellationBeforeExecutionPreventsTheDelegate()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var executed = false;

		await Assert.ThrowsAsync<OperationCanceledException>(async () => await scheduler.InvokeAsync(() =>
		{
			executed = true;

			return true;
		}, cancellation.Token));

		Assert.IsFalse(executed);
	}

	/// <summary>Verifies cancellation completes queued work without running its delegate.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task CancellationWhileQueuedPreventsTheDelegate()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		using var cancellation = new CancellationTokenSource();
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var active = scheduler.InvokeAsync(() =>
		{
			started.SetResult(true);
			WaitForRelease(release);

			return true;
		});
		var executed = false;

		try
		{
			await started.Task.WaitAsync(_testTimeout);
			var pending = scheduler.InvokeAsync(() =>
			{
				executed = true;

				return true;
			}, cancellation.Token);
			cancellation.Cancel();
			await Assert.ThrowsAsync<TaskCanceledException>(async () => await pending.WaitAsync(_testTimeout));
		}
		finally
		{
			release.Set();
		}

		await active.WaitAsync(_testTimeout);
		await scheduler.InvokeAsync(() => true).WaitAsync(_testTimeout);
		Assert.IsFalse(executed);
	}

	/// <summary>Verifies cancellation does not replace an active delegate's result.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task CancellationAfterExecutionStartsPreservesResult()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		using var cancellation = new CancellationTokenSource();
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var active = scheduler.InvokeAsync(() =>
		{
			started.SetResult(true);
			WaitForRelease(release);

			return 42;
		}, cancellation.Token);

		try
		{
			await started.Task.WaitAsync(_testTimeout);
			cancellation.Cancel();
			Assert.IsFalse(active.IsCompleted);
		}
		finally
		{
			release.Set();
		}

		Assert.AreEqual(42, await active.WaitAsync(_testTimeout));
	}

	/// <summary>Verifies disposal waits for an active delegate to finish.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task DisposalWaitsForAnActiveDelegate()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var active = scheduler.InvokeAsync(() =>
		{
			started.SetResult(true);
			WaitForRelease(release);

			return true;
		});
		Task disposal;

		try
		{
			await started.Task.WaitAsync(_testTimeout);
			disposal = scheduler.DisposeAsync().AsTask();
			Assert.IsFalse(disposal.IsCompleted);
		}
		finally
		{
			release.Set();
		}

		Assert.IsTrue(await active.WaitAsync(_testTimeout));
		await disposal.WaitAsync(_testTimeout);
	}

	/// <summary>Verifies disposal faults pending work and rejects new submissions.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task DisposalFaultsPendingWorkAndRejectsNewWork()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var active = scheduler.InvokeAsync(() =>
		{
			started.SetResult(true);
			WaitForRelease(release);

			return true;
		});
		var executed = false;
		Task disposal;

		try
		{
			await started.Task.WaitAsync(_testTimeout);
			var pending = scheduler.InvokeAsync(() =>
			{
				executed = true;

				return true;
			});
			disposal = scheduler.DisposeAsync().AsTask();
			await Assert.ThrowsAsync<ObjectDisposedException>(async () => await pending.WaitAsync(_testTimeout));
			await Assert.ThrowsAsync<ObjectDisposedException>(async () => await scheduler.InvokeAsync(() => true).WaitAsync(_testTimeout));
			Assert.IsFalse(disposal.IsCompleted);
		}
		finally
		{
			release.Set();
		}

		await active.WaitAsync(_testTimeout);
		await disposal.WaitAsync(_testTimeout);
		Assert.IsFalse(executed);
	}

	/// <summary>Verifies concurrent disposal callers share the same completion task.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task ConcurrentDisposalsShareCompletionTask()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var active = scheduler.InvokeAsync(() =>
		{
			started.SetResult(true);
			WaitForRelease(release);

			return true;
		});
		Task[] disposals;

		try
		{
			await started.Task.WaitAsync(_testTimeout);
			var requests = Enumerable.Range(0, 8).Select(_ => Task.Run(() => scheduler.DisposeAsync()));
			var results = await Task.WhenAll(requests).WaitAsync(_testTimeout);
			disposals = results.Select(result => result.AsTask()).ToArray();

			foreach (var disposal in disposals)
			{
				Assert.AreSame(disposals[0], disposal);
				Assert.IsFalse(disposal.IsCompleted);
			}
		}
		finally
		{
			release.Set();
		}

		await active.WaitAsync(_testTimeout);
		await Task.WhenAll(disposals).WaitAsync(_testTimeout);
	}

	/// <summary>Verifies queued work runs before a message backlog is fully drained.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task MessageBacklogDoesNotStarveQueuedWork()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
		var active = scheduler.InvokeAsync(() =>
		{
			started.SetResult(NativeMethods.GetCurrentThreadId());
			WaitForRelease(release);

			return true;
		});
		Task<bool> pending;

		try
		{
			var thread = await started.Task.WaitAsync(_testTimeout);
			pending = scheduler.InvokeAsync(() => (bool)NativeMethods.PeekMessage(out _, default, NativeMethods.WM_APP, NativeMethods.WM_APP, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE));

			for (var index = 0; index < 512; index++)
			{
				Assert.IsTrue(NativeMethods.PostThreadMessage(thread, NativeMethods.WM_APP, default, default), "Failed to post a test message.");
			}
		}
		finally
		{
			release.Set();
		}

		await active.WaitAsync(_testTimeout);
		Assert.IsTrue(await pending.WaitAsync(_testTimeout), "The message pump drained the entire backlog before running queued work.");
	}

	/// <summary>Verifies WM_QUIT faults pending and future work on the affected lane.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task QuitMessageFaultsPendingAndFutureWork()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var active = scheduler.InvokeAsync(() =>
		{
			started.SetResult(true);
			WaitForRelease(release);
			NativeMethods.PostQuitMessage(0);

			return true;
		});
		var executed = false;
		Task<bool> pending;

		try
		{
			await started.Task.WaitAsync(_testTimeout);
			pending = scheduler.InvokeAsync(() =>
			{
				executed = true;

				return true;
			});
		}
		finally
		{
			release.Set();
		}

		await active.WaitAsync(_testTimeout);
		await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending.WaitAsync(_testTimeout));
		await Assert.ThrowsAsync<InvalidOperationException>(async () => await scheduler.InvokeAsync(() => true).WaitAsync(_testTimeout));
		Assert.IsFalse(executed);
		Assert.IsTrue(await scheduler.InvokeConcurrentAsync(() => true).WaitAsync(_testTimeout));
	}

	/// <summary>Verifies a delegate failure does not stop later work on the lane.</summary>
	/// <returns>A task that represents the test.</returns>
	[TestMethod]
	public async Task DelegateFailureDoesNotStopLane()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		await Assert.ThrowsAsync<ArgumentException>(async () => await scheduler.InvokeAsync<bool>(() => throw new ArgumentException("Expected test failure.")).WaitAsync(_testTimeout));
		Assert.IsTrue(await scheduler.InvokeAsync(() => true).WaitAsync(_testTimeout));
	}

	private static Task<T> InvokeOnLane<T>(WindowsShellScheduler scheduler, string lane, Func<T> action)
	{
		return lane switch
		{
			"ordered" => scheduler.InvokeAsync(action),
			"concurrent" => scheduler.InvokeConcurrentAsync(action),
			"search" => scheduler.InvokeSearchAsync(action),
			"operation" => scheduler.InvokeOperationAsync(action),
			_ => throw new ArgumentOutOfRangeException(nameof(lane)),
		};
	}

	private static void WaitForRelease(ManualResetEventSlim release)
	{
		Assert.IsTrue(release.Wait(_testTimeout), "The test did not release the active delegate.");
	}

	private static void SpinUpdateMaximum(ref int target, int candidate)
	{
		while (true)
		{
			var current = Volatile.Read(ref target);

			if (candidate <= current || Interlocked.CompareExchange(ref target, candidate, current) == current)
			{
				return;
			}
		}
	}
}
