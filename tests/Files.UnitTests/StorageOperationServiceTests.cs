// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for storage operation service behavior.
/// </summary>
[TestClass]
public sealed class StorageOperationServiceTests
{
	/// <summary>
	/// Test case: selects first handler that can handle the request.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SelectsFirstHandlerThatCanHandleTheRequest()
	{
		var request = CreateRenameRequest();
		var first = new TestOperationHandler(canHandle: false);
		var second = new TestOperationHandler(canHandle: true);
		var service = new StorageOperationService([first, second]);

		Assert.IsTrue(service.CanHandle(request));
		var result = await service.ExecuteAsync(request);

		Assert.IsTrue(result.Succeeded);
		Assert.AreEqual(0, first.ExecuteCount);
		Assert.AreEqual(1, second.ExecuteCount);
	}

	/// <summary>
	/// Test case: reports unsupported request as failed result.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ReportsUnsupportedRequestAsFailedResult()
	{
		var service = new StorageOperationService([new TestOperationHandler(canHandle: false)]);

		Assert.IsFalse(service.CanHandle(new UnknownOperationRequest()));
		var result = await service.ExecuteAsync(new UnknownOperationRequest());

		Assert.IsFalse(result.Succeeded);
		Assert.IsInstanceOfType<NotSupportedException>(result.Error);
		Assert.IsNull(result.ResultItem);
	}

	/// <summary>
	/// Test case: maps handler exception to failed result.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task MapsHandlerExceptionToFailedResult()
	{
		var expected = new IOException("operation failed");
		var handler = new TestOperationHandler(canHandle: true, exception: expected);
		var service = new StorageOperationService([handler]);

		var result = await service.ExecuteAsync(CreateRenameRequest());

		Assert.IsFalse(result.Succeeded);
		Assert.AreSame(expected, result.Error);
	}

	/// <summary>
	/// Test case: propagates cancellation before handler execution.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task PropagatesCancellationBeforeHandlerExecution()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var handler = new TestOperationHandler(canHandle: true);
		var service = new StorageOperationService([handler]);

		await Assert.ThrowsAsync<OperationCanceledException>(async () => await service.ExecuteAsync(CreateRenameRequest(), cancellationToken: cancellation.Token));

		Assert.AreEqual(0, handler.ExecuteCount);
	}

	/// <summary>
	/// Test case: maps null handler result to failed result.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task MapsNullHandlerResultToFailedResult()
	{
		var service = new StorageOperationService([new NullOperationHandler()]);

		var result = await service.ExecuteAsync(CreateRenameRequest());

		Assert.IsFalse(result.Succeeded);
		Assert.IsInstanceOfType<InvalidOperationException>(result.Error);
	}

	/// <summary>
	/// Test case: forwards cooperative operation control to the selected handler.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ForwardsOperationControlToHandler()
	{
		var handler = new TestOperationHandler(canHandle: true);
		var service = new StorageOperationService([handler]);
		var operationControl = new TestOperationControl(isPauseRequested: true);

		var result = await service.ExecuteAsync(CreateRenameRequest(), operationControl: operationControl);

		Assert.IsTrue(result.Succeeded);
		Assert.AreSame(operationControl, handler.OperationControl);
	}

	/// <summary>
	/// Test case: requests reject unknown enum values.
	/// </summary>
	[TestMethod]
	public void RequestsRejectUnknownEnumValues()
	{
		var reference = CreateRenameRequest().Item;

		Assert.Throws<ArgumentOutOfRangeException>(() => new CreateItemOperationRequest(reference, "item", (StorageItemKind)int.MaxValue));
		Assert.Throws<ArgumentOutOfRangeException>(() => new CopyOperationRequest(reference, reference, conflictBehavior: (StorageConflictBehavior)int.MaxValue));
	}

	/// <summary>
	/// Test case: result and progress reject contradictory state.
	/// </summary>
	[TestMethod]
	public void ResultAndProgressRejectContradictoryState()
	{
		var reference = CreateRenameRequest().Item;

		Assert.Throws<ArgumentException>(() => new StorageOperationResult(succeeded: true, resultItem: reference, error: new IOException("unexpected")));
		Assert.Throws<ArgumentNullException>(() => new StorageOperationResult(succeeded: false, resultItem: null));
		Assert.Throws<ArgumentOutOfRangeException>(() => new StorageOperationProgress(completedItems: 2, totalItems: 1));
		Assert.Throws<ArgumentException>(() => new StorageOperationProgress(0, 1, completedBytes: 1));
		Assert.Throws<ArgumentOutOfRangeException>(() => new StorageOperationProgress(0, 1, completedBytes: 2, totalBytes: 1));
	}

	private static RenameOperationRequest CreateRenameRequest()
	{
		return new RenameOperationRequest(new StorableReference(new StorageSourceId("test"), "item-1", new StorageAddress("test", "item-1")), "renamed.txt");
	}

	private sealed record UnknownOperationRequest : StorageOperationRequest;

	private sealed class NullOperationHandler : IStorageOperationHandler
	{
		public bool CanHandle(StorageOperationRequest request) => true;

		public ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default,
			IStorageOperationControl? operationControl = null)
		{
			return ValueTask.FromResult<StorageOperationResult>(null!);
		}
	}

	private sealed class TestOperationHandler : IStorageOperationHandler
	{
		private readonly bool _canHandle;

		private readonly Exception? _exception;

		public int ExecuteCount { get; private set; }

		public IStorageOperationControl? OperationControl { get; private set; }

		public TestOperationHandler(bool canHandle, Exception? exception = null)
		{
			_canHandle = canHandle;
			_exception = exception;
		}

		public bool CanHandle(StorageOperationRequest request) => _canHandle;

		public ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default,
			IStorageOperationControl? operationControl = null)
		{
			ExecuteCount++;
			OperationControl = operationControl;
			if (_exception is not null)
			{
				throw _exception;
			}

			return ValueTask.FromResult(new StorageOperationResult(true, null));
		}
	}

	private sealed class TestOperationControl(bool isPauseRequested) : IStorageOperationControl
	{
		public bool IsPauseRequested { get; } = isPauseRequested;

		public void AcknowledgePauseState(bool isPaused)
		{
		}

		public void AcknowledgeCancellationRequest()
		{
		}
	}
}
