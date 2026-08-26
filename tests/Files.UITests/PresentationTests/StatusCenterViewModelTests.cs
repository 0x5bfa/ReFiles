// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Infrastructure;
using Files.StorageOperations;
using Files.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
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

		progress.Report(new StorageOperationProgress(1, 1, completedBytes: 1000, totalBytes: 1000));
		var completedItemSnapshot = tracker.GetSnapshot()[0];
		Assert.AreEqual(2, completedItemSnapshot.CompletedItems);
		Assert.IsNull(completedItemSnapshot.CompletedBytes);
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
