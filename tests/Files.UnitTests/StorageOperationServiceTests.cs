// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.UnitTests;

[TestClass]
public sealed class StorageOperationServiceTests
{
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

	[TestMethod]
	public async Task MapsNullHandlerResultToFailedResult()
	{
		var service = new StorageOperationService([new NullOperationHandler()]);

		var result = await service.ExecuteAsync(CreateRenameRequest());

		Assert.IsFalse(result.Succeeded);
		Assert.IsInstanceOfType<InvalidOperationException>(result.Error);
	}

	[TestMethod]
	public void RequestsRejectUnknownEnumValues()
	{
		var reference = CreateRenameRequest().Item;

		Assert.Throws<ArgumentOutOfRangeException>(() => new CreateItemOperationRequest(reference, "item", (StorageItemKind)int.MaxValue));
		Assert.Throws<ArgumentOutOfRangeException>(() => new CopyOperationRequest(reference, reference, conflictBehavior: (StorageConflictBehavior)int.MaxValue));
	}

	[TestMethod]
	public void ResultAndProgressRejectContradictoryState()
	{
		var reference = CreateRenameRequest().Item;

		Assert.Throws<ArgumentException>(() => new StorageOperationResult(Succeeded: true, ResultItem: reference, Error: new IOException("unexpected")));
		Assert.Throws<ArgumentNullException>(() => new StorageOperationResult(Succeeded: false, ResultItem: null));
		Assert.Throws<ArgumentOutOfRangeException>(() => new StorageOperationProgress(completedItems: 2, totalItems: 1));
	}

	private static RenameOperationRequest CreateRenameRequest()
	{
		return new RenameOperationRequest(new StorableReference(new StorageSourceId("test"), "item-1", new StorageAddress("test", "item-1")), "renamed.txt");
	}

	private sealed record UnknownOperationRequest : StorageOperationRequest;

	private sealed class NullOperationHandler : IStorageOperationHandler
	{
		public bool CanHandle(StorageOperationRequest request) => true;

		public ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult<StorageOperationResult>(null!);
		}
	}

	private sealed class TestOperationHandler : IStorageOperationHandler
	{
		private readonly bool canHandle;

		private readonly Exception? exception;

		public int ExecuteCount { get; private set; }

		public TestOperationHandler(bool canHandle, Exception? exception = null)
		{
			this.canHandle = canHandle;
			this.exception = exception;
		}

		public bool CanHandle(StorageOperationRequest request) => canHandle;

		public ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default)
		{
			ExecuteCount++;
			if (exception is not null)
			{
				throw exception;
			}

			return ValueTask.FromResult(new StorageOperationResult(true, null));
		}
	}
}
