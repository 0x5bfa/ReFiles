// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.UnitTests;

/// <summary>Verifies independent job submission and observation workflows.</summary>
[TestClass]
public sealed class StorageJobExecutionTests
{
	/// <summary>Retains an accepted job when its observer detaches.</summary>
	/// <returns>The test task.</returns>
	[TestMethod]
	public async Task CanceledObservationRetainsAcceptedJobForAnotherWaiter()
	{
		var service = new JobService();
		using var cancellation = new CancellationTokenSource();
		var pending = service.ExecuteAndWaitAsync([new RenameOperationRequest(new StorableReference(new StorageSourceId("test"), "item"), "renamed")], cancellation.Token).AsTask();
		await service.Accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.IsFalse(pending.IsCompleted);
		cancellation.Cancel();
		var error = await Assert.ThrowsAsync<StorageJobObservationCanceledException>(() => pending);
		Assert.AreEqual(service.Handle, error.Job);
		Assert.AreEqual(1, service.SubmissionCount);
		service.Completion.SetResult(new StorageOperationResult(true, null));
		Assert.IsTrue((await service.WaitForCompletionAsync(error.Job)).Succeeded);
		Assert.AreEqual(1, service.SubmissionCount);
	}

	/// <summary>Never retries an uncertain submission.</summary>
	/// <returns>The test task.</returns>
	[TestMethod]
	public async Task LostAcceptanceReplyDoesNotRetryOrStartObservation()
	{
		var service = new JobService { LoseReply = true };
		var request = new RenameOperationRequest(new StorableReference(new StorageSourceId("test"), "item"), "renamed");
		var error = await Assert.ThrowsAsync<StorageJobUnknownException>(async () => await service.ExecuteAndWaitAsync([request]));
		Assert.IsFalse(error.Accepted);
		Assert.AreEqual(service.Handle, error.Job);
		Assert.AreNotEqual(Guid.Empty, error.Job.Id);
		Assert.AreEqual(1, service.SubmissionCount);
		Assert.AreEqual(0, service.ObservationCount);
	}

	private sealed class JobService : IStorageOperationService, IStorageJobService
	{
		public TaskCompletionSource Accepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource<StorageOperationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public IStorageJobService Jobs => this;
		public StorageJobHandle Handle { get; private set; } = new(Guid.Empty, Guid.NewGuid());
		public bool LoseReply { get; init; }
		public int SubmissionCount { get; private set; }
		public int ObservationCount { get; private set; }

		public bool CanHandle(StorageOperationRequest request) => true;

		public ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default,
			IStorageOperationControl? operationControl = null)
		{
			throw new AssertFailedException("Independent jobs must not use inline execution.");
		}

		public ValueTask<StorageJobHandle> SubmitBatchAsync(Guid id, IReadOnlyList<StorageOperationRequest> requests, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			SubmissionCount++;
			Handle = Handle with { Id = id };
			Accepted.SetResult();
			if (LoseReply)
			{
				throw new StorageJobUnknownException(Handle, false, "Lost reply");
			}

			return ValueTask.FromResult(Handle);
		}

		public async ValueTask<StorageOperationResult> WaitForCompletionAsync(StorageJobHandle job, CancellationToken cancellationToken = default)
		{
			Assert.AreEqual(Handle, job);
			ObservationCount++;

			return await Completion.Task.WaitAsync(cancellationToken);
		}
	}
}
