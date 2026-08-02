// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class WindowsShellSchedulerTests
{
	[TestMethod]
	public async Task OrderedLaneRunsOnStaAndPreservesFifoOrder()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		var order = new ConcurrentQueue<int>();

		var tasks = Enumerable.Range(0, 16)
			.Select(index => scheduler.InvokeAsync(() =>
			{
				Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
				order.Enqueue(index);
				return index;
			}))
			.ToArray();

		await Task.WhenAll(tasks);
		CollectionAssert.AreEqual(Enumerable.Range(0, 16).ToArray(), order.ToArray());
	}

	[TestMethod]
	public async Task ConcurrentLaneDoesNotExceedConfiguredWorkerCount()
	{
		await using var scheduler = new WindowsShellScheduler(concurrentWorkerCount: 2);
		using var release = new ManualResetEventSlim(false);
		var active = 0;
		var maximumActive = 0;

		var tasks = Enumerable.Range(0, 8)
			.Select(_ => scheduler.InvokeConcurrentAsync(() =>
			{
				var current = Interlocked.Increment(ref active);
				SpinUpdateMaximum(ref maximumActive, current);
				try
				{
					release.Wait();
					return current;
				}
				finally
				{
					Interlocked.Decrement(ref active);
				}
			}))
			.ToArray();

		Assert.IsTrue(
			SpinWait.SpinUntil(() => Volatile.Read(ref maximumActive) == 2, TimeSpan.FromSeconds(5)),
			"The configured concurrent workers did not reach the expected parallelism.");
		release.Set();
		await Task.WhenAll(tasks);

		Assert.AreEqual(2, maximumActive);
	}

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
				threadIds.Add(Thread.CurrentThread.ManagedThreadId);
				apartmentStates.Add(Thread.CurrentThread.GetApartmentState());
				Thread.Yield();
			}

			return true;
		});

		Assert.AreEqual(1, threadIds.Distinct().Count());
		Assert.AreEqual(1, apartmentStates.Distinct().Count());
		Assert.AreEqual(ApartmentState.STA, apartmentStates.Distinct().Single());
	}

	[TestMethod]
	public async Task NestedOrderedInvocationRunsWithoutDeadlock()
	{
		await using var scheduler = new WindowsShellScheduler();

		var result = await scheduler.InvokeAsync(() =>
			scheduler.InvokeAsync(() =>
				(Thread.CurrentThread.GetApartmentState(), Thread.CurrentThread.ManagedThreadId))
			.GetAwaiter()
			.GetResult());

		Assert.AreEqual(ApartmentState.STA, result.Item1);
}

	[TestMethod]
	public async Task CancellationBeforeExecutionPreventsTheDelegate()
	{
		await using var scheduler = new WindowsShellScheduler();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var executed = false;

		await Assert.ThrowsAsync<OperationCanceledException>(async () =>
			await scheduler.InvokeAsync(
				() =>
				{
					executed = true;
					return true;
				},
				cancellation.Token));

		Assert.IsFalse(executed);
}

	[TestMethod]
	public async Task DisposalWaitsForAnActiveDelegate()
	{
		var scheduler = new WindowsShellScheduler();
		var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		var work = scheduler.InvokeAsync(() =>
		{
			started.SetResult(true);
			release.Task.GetAwaiter().GetResult();
			return true;
		});

		await started.Task;
		var disposeTask = scheduler.DisposeAsync().AsTask();
		Assert.IsFalse(disposeTask.IsCompleted);
		release.SetResult(true);

		Assert.IsTrue(await work);
		await disposeTask;
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
