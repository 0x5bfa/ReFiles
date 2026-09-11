// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UITests.ControlTests;

/// <summary>Verifies search debounce scheduling and cancellation without real-time waits.</summary>
[TestClass]
public sealed class SearchTextDebouncerTests
{
	/// <summary>Verifies that scheduling a newer query cancels the older query.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	public async Task NewScheduleCancelsPreviousQuery()
	{
		var firstDelayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondDelayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var delayCalls = 0;
		var executedQueries = new List<string>();
		Func<TimeSpan, CancellationToken, Task> delay = async (_, cancellationToken) =>
		{
			var call = Interlocked.Increment(ref delayCalls);
			if (call is 1)
			{
				firstDelayStarted.SetResult();
			}
			else if (call is 2)
			{
				secondDelayStarted.SetResult();
			}

			var release = call is 1 ? firstRelease.Task : secondRelease.Task;
			await release.WaitAsync(cancellationToken);
		};
		using var debouncer = new SearchTextDebouncer(TimeSpan.FromMilliseconds(200), delay);

		var first = debouncer.ScheduleAsync("first", (query, _) =>
		{
			executedQueries.Add(query);

			return Task.CompletedTask;
		});
		await firstDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

		var second = debouncer.ScheduleAsync("second", (query, _) =>
		{
			executedQueries.Add(query);

			return Task.CompletedTask;
		});
		await secondDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		secondRelease.SetResult();
		await Task.WhenAll(first, second);

		CollectionAssert.AreEqual(new[] { "second" }, executedQueries);
	}

	/// <summary>Verifies that canceling a pending query prevents its action from running.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	public async Task CancelPreventsPendingQueryExecution()
	{
		var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var executed = false;
		async Task Delay(TimeSpan _, CancellationToken cancellationToken)
		{
			delayStarted.SetResult();
			await release.Task.WaitAsync(cancellationToken);
		}
		using var debouncer = new SearchTextDebouncer(TimeSpan.FromMilliseconds(200), Delay);

		var pending = debouncer.ScheduleAsync("query", (_, _) =>
		{
			executed = true;

			return Task.CompletedTask;
		});
		await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

		debouncer.Cancel();
		release.SetResult();
		await pending;

		Assert.IsFalse(executed);
	}

	/// <summary>Verifies that a disposed debouncer ignores new schedules.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	public async Task DisposePreventsFutureQueryExecution()
	{
		using var debouncer = new SearchTextDebouncer(TimeSpan.Zero);
		debouncer.Dispose();
		var executed = false;

		await debouncer.ScheduleAsync("query", (_, _) =>
		{
			executed = true;

			return Task.CompletedTask;
		});

		Assert.IsFalse(executed);
	}
}
