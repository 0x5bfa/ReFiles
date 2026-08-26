// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Infrastructure;
using Files.StorageOperations;
using Files.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System.Diagnostics;
using System.IO;

namespace Files.UITests;

/// <summary>
/// Verifies storage operation tracking and Status Center presentation.
/// </summary>
[TestClass]
public sealed class StatusCenterViewModelTests
{
	/// <summary>
	/// Verifies that batch progress is mapped to the containing operation.
	/// </summary>
	[TestMethod]
	public void AggregatesProgressAcrossBatchItems()
	{
		using var tracker = new StorageOperationTracker();
		var operation = tracker.StartOperation(TrackedStorageOperationKind.Copy, 3, "first.txt", canCancel: false);
		var progress = new StorageOperationBatchProgress(operation, completedItems: 1, "second.txt");

		progress.Report(new StorageOperationProgress(0, 1, completedBytes: 250, totalBytes: 1000));
		var runningSnapshot = tracker.GetSnapshot()[0];
		Assert.AreEqual(1, runningSnapshot.CompletedItems);
		Assert.AreEqual(250, runningSnapshot.CompletedBytes);
		Assert.AreEqual(1000, runningSnapshot.TotalBytes);
		Assert.AreEqual(41.67f, runningSnapshot.SpeedGraphPoints[^1].X, 0.01f);

		progress.Report(new StorageOperationProgress(1, 1, completedBytes: 1000, totalBytes: 1000));
		var completedItemSnapshot = tracker.GetSnapshot()[0];
		Assert.AreEqual(2, completedItemSnapshot.CompletedItems);
		Assert.IsNull(completedItemSnapshot.CompletedBytes);
	}

	/// <summary>
	/// Verifies that item-only operations publish a rate graph when byte totals are unavailable.
	/// </summary>
	[TestMethod]
	public void PublishesItemRateGraphWithoutByteProgress()
	{
		using var tracker = new StorageOperationTracker();
		var operation = tracker.StartOperation(TrackedStorageOperationKind.Delete, 2, "first.bin", canCancel: false);

		operation.Report(0, "first.bin");
		operation.Report(1, "second.bin");

		var points = tracker.GetSnapshot()[0].SpeedGraphPoints;
		Assert.IsTrue(points.Count > 1);
		Assert.AreEqual(50f, points[^1].X, 0.01f);
		Assert.IsTrue(float.IsFinite(points[^1].Y));
	}

	/// <summary>
	/// Verifies that known file sizes produce byte-weighted progress across an entire batch.
	/// </summary>
	[UITestMethod]
	public void AggregatesByteProgressAcrossUnequalBatchItems()
	{
		using var tracker = new StorageOperationTracker();
		var dispatcher = new DispatcherQueueUIDispatcher(UnitTestApp.TestDispatcherQueue);
		using var viewModel = new StatusCenterViewModel(tracker, dispatcher);
		var operation = tracker.StartOperation(TrackedStorageOperationKind.Copy, 2, "small.bin", canCancel: false);
		var progress = new StorageOperationBatchProgress(operation, completedItems: 1, "large.bin", completedBytesBeforeCurrentItem: 10, currentItemBytes: 90, totalBatchBytes: 100);

		progress.Report(new StorageOperationProgress(0, 1, completedBytes: 45, totalBytes: 90));

		var snapshot = tracker.GetSnapshot()[0];
		Assert.IsTrue(snapshot.IsByteProgressForWholeOperation);
		Assert.AreEqual(55L, snapshot.CompletedBytes);
		Assert.AreEqual(100L, snapshot.TotalBytes);
		Assert.AreEqual(55d, viewModel.Items[0].ProgressPercentage, 0.01);
	}

	/// <summary>
	/// Verifies that the speed graph retains finite, ordered samples without growing for every progress callback.
	/// </summary>
	[UITestMethod]
	public void BoundsSpeedGraphHistory()
	{
		using var tracker = new StorageOperationTracker();
		var dispatcher = new DispatcherQueueUIDispatcher(UnitTestApp.TestDispatcherQueue);
		using var viewModel = new StatusCenterViewModel(tracker, dispatcher);
		var operation = tracker.StartOperation(TrackedStorageOperationKind.Copy, 1, "large.bin", canCancel: false);
		const int sampleCount = 1024;
		const long totalBytes = sampleCount * 1024L;

		operation.Report(0, "large.bin", completedBytes: 0, totalBytes: totalBytes, isByteProgressForWholeOperation: true);
		for (var index = 1; index <= sampleCount; index++)
		{
			operation.Report(0, "large.bin", completedBytes: index * 1024L, totalBytes: totalBytes, isByteProgressForWholeOperation: true);
		}

		var item = viewModel.Items[0];
		var points = item.SpeedGraphPoints.ToArray();
		Assert.IsTrue(item.HasSpeedGraphPoints);
		Assert.IsTrue(points.Length > 1);
		Assert.IsTrue(points.Length <= 201, $"The graph retained {points.Length} samples for {sampleCount} callbacks.");
		Assert.IsTrue(points.All(static point => float.IsFinite(point.X) && float.IsFinite(point.Y)));
		Assert.IsTrue(points.Zip(points.Skip(1)).All(static pair => pair.First.X <= pair.Second.X));
	}

	/// <summary>
	/// Verifies that a short-lived transfer-rate spike is smoothed before it reaches the graph data.
	/// </summary>
	[UITestMethod]
	public async Task SmoothsTransferRateSpikes()
	{
		using var tracker = new StorageOperationTracker();
		var dispatcher = new DispatcherQueueUIDispatcher(UnitTestApp.TestDispatcherQueue);
		using var viewModel = new StatusCenterViewModel(tracker, dispatcher);
		var operation = tracker.StartOperation(TrackedStorageOperationKind.Copy, 1, "large.bin", canCancel: false);
		const long totalBytes = 200_000_000;

		operation.Report(0, "large.bin", completedBytes: 0, totalBytes: totalBytes, isByteProgressForWholeOperation: true);
		await Task.Delay(100);
		operation.Report(0, "large.bin", completedBytes: 1_000_000, totalBytes: totalBytes, isByteProgressForWholeOperation: true);
		await Task.Delay(100);
		operation.Report(0, "large.bin", completedBytes: 2_000_000, totalBytes: totalBytes, isByteProgressForWholeOperation: true);
		var spikeSampleTimestamp = Stopwatch.GetTimestamp();
		await Task.Delay(100);
		operation.Report(0, "large.bin", completedBytes: 102_000_000, totalBytes: totalBytes, isByteProgressForWholeOperation: true);

		var spikeElapsedSeconds = Stopwatch.GetElapsedTime(spikeSampleTimestamp).TotalSeconds;
		var rawSpikeSpeed = 100_000_000d / spikeElapsedSeconds;
		var snapshot = tracker.GetSnapshot()[0];
		Assert.IsNotNull(snapshot.BytesPerSecond);
		Assert.IsTrue(snapshot.BytesPerSecond.Value < rawSpikeSpeed);
		Assert.IsTrue(viewModel.Items[0].HasSpeedGraphPoints);
		Assert.IsTrue(viewModel.Items[0].SpeedGraphPoints.All(static point => float.IsFinite(point.Y) && point.Y >= 0));
	}

	/// <summary>
	/// Verifies that byte progress is projected as percentage, speed, and remaining time without blocking the dispatcher.
	/// </summary>
	[UITestMethod]
	public async Task PublishesTransferRateAndRemainingTime()
	{
		using var tracker = new StorageOperationTracker();
		var dispatcher = new DispatcherQueueUIDispatcher(UnitTestApp.TestDispatcherQueue);
		using var viewModel = new StatusCenterViewModel(tracker, dispatcher);
		var operation = tracker.StartOperation(TrackedStorageOperationKind.Copy, 1, "large.iso", canCancel: true);
		var progress = new StorageOperationBatchProgress(operation, completedItems: 0, "large.iso");
		const long totalBytes = 100 * 1024 * 1024;

		progress.Report(new StorageOperationProgress(0, 1, completedBytes: 0, totalBytes: totalBytes));
		var item = viewModel.Items[0];
		await Task.Delay(120);
		progress.Report(new StorageOperationProgress(0, 1, completedBytes: totalBytes / 2, totalBytes: totalBytes));

		Assert.AreSame(item, viewModel.Items[0]);
		Assert.AreEqual(50d, viewModel.Items[0].ProgressPercentage, 0.01);
		StringAssert.Contains(viewModel.Items[0].ProgressText, "50.0 MB");
		StringAssert.Contains(viewModel.Items[0].ProgressText, "/s");
		StringAssert.Contains(viewModel.Items[0].ProgressText, "remaining");
		Assert.IsTrue(viewModel.Items[0].CanCancel);
	}

	/// <summary>
	/// Verifies that each window projection receives shared operation progress and completion.
	/// </summary>
	[UITestMethod]
	public void PublishesProgressToEachWindowProjection()
	{
		using var tracker = new StorageOperationTracker();
		var dispatcher = new DispatcherQueueUIDispatcher(UnitTestApp.TestDispatcherQueue);
		using var firstViewModel = new StatusCenterViewModel(tracker, dispatcher);
		using var secondViewModel = new StatusCenterViewModel(tracker, dispatcher);
		var operation = tracker.StartOperation(TrackedStorageOperationKind.Move, 2, "first.txt", canCancel: false);

		operation.Report(1, "second.txt");

		Assert.AreEqual(1, firstViewModel.Items.Count);
		Assert.AreEqual(1, secondViewModel.Items.Count);
		Assert.AreEqual(50d, firstViewModel.AverageProgressPercentage);
		Assert.AreEqual("second.txt", firstViewModel.Items[0].Detail);

		operation.Complete();

		Assert.IsFalse(firstViewModel.HasInProgressItems);
		Assert.IsTrue(firstViewModel.HasCompletedItems);
		Assert.AreEqual(100d, firstViewModel.Items[0].ProgressPercentage);
		firstViewModel.ClearCompleted();
		Assert.IsFalse(firstViewModel.HasItems);
		Assert.IsFalse(secondViewModel.HasItems);
	}

	/// <summary>
	/// Verifies that canceling a card cancels its operation token and publishes a terminal state.
	/// </summary>
	[UITestMethod]
	public void CancelsAnOperationFromItsCard()
	{
		using var tracker = new StorageOperationTracker();
		var dispatcher = new DispatcherQueueUIDispatcher(UnitTestApp.TestDispatcherQueue);
		using var viewModel = new StatusCenterViewModel(tracker, dispatcher);
		var operation = tracker.StartOperation(TrackedStorageOperationKind.Delete, 1, "item.txt", canCancel: true);
		var operationId = viewModel.Items[0].Id;

		viewModel.Cancel(operationId);

		Assert.IsTrue(operation.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(viewModel.Items[0].CanCancel);

		operation.MarkCanceled();

		Assert.IsFalse(viewModel.Items[0].IsRunning);
		Assert.IsTrue(viewModel.Items[0].CanRemove);
		viewModel.Remove(operationId);
		Assert.AreEqual(0, viewModel.Items.Count);
	}

	/// <summary>
	/// Verifies that simultaneous operations keep stable cards and independent cancellation tokens.
	/// </summary>
	[UITestMethod]
	public void CancelsOnlyTheSelectedSimultaneousOperation()
	{
		using var tracker = new StorageOperationTracker();
		var dispatcher = new DispatcherQueueUIDispatcher(UnitTestApp.TestDispatcherQueue);
		using var viewModel = new StatusCenterViewModel(tracker, dispatcher);
		var firstOperation = tracker.StartOperation(TrackedStorageOperationKind.Copy, 1, "first.iso", canCancel: true);
		var secondOperation = tracker.StartOperation(TrackedStorageOperationKind.Copy, 1, "second.iso", canCancel: true);
		var firstCard = viewModel.Items.Single(item => item.Detail == "first.iso");
		var secondCard = viewModel.Items.Single(item => item.Detail == "second.iso");

		viewModel.Cancel(firstCard.Id);
		secondOperation.Report(0, "second.iso", completedBytes: 50, totalBytes: 100);

		Assert.AreEqual(2, viewModel.Items.Count);
		Assert.AreSame(firstCard, viewModel.Items.Single(item => item.Id == firstCard.Id));
		Assert.AreSame(secondCard, viewModel.Items.Single(item => item.Id == secondCard.Id));
		Assert.IsTrue(firstOperation.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(secondOperation.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(firstCard.CanCancel);
		Assert.IsTrue(secondCard.CanCancel);
	}

	/// <summary>
	/// Verifies that failures retain their error details until dismissed.
	/// </summary>
	[UITestMethod]
	public void PublishesAnOperationFailure()
	{
		using var tracker = new StorageOperationTracker();
		var dispatcher = new DispatcherQueueUIDispatcher(UnitTestApp.TestDispatcherQueue);
		using var viewModel = new StatusCenterViewModel(tracker, dispatcher);
		var operation = tracker.StartOperation(TrackedStorageOperationKind.Copy, 1, "item.txt", canCancel: false);

		operation.Fail(new IOException("The destination is unavailable."));

		Assert.IsFalse(viewModel.Items[0].IsRunning);
		Assert.AreEqual("The destination is unavailable.", viewModel.Items[0].Detail);
		Assert.IsTrue(viewModel.HasCompletedItems);
	}
}
