// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Accepts independent jobs and observes them without controlling their lifetime.</summary>
public interface IStorageJobService
{
	/// <summary>Submits a complete batch under a caller-selected identity without waiting for completion.</summary>
	/// <param name="id">The nonempty identity, retained even if the acceptance reply is lost. Never resubmit automatically.</param>
	/// <param name="requests">The complete operation intent.</param>
	/// <param name="cancellationToken">Cancels preparation before dispatch; dispatch is awaited independently.</param>
	/// <returns>The accepted job identity and execution server identity.</returns>
	ValueTask<StorageJobHandle> SubmitBatchAsync(Guid id, IReadOnlyList<StorageOperationRequest> requests, CancellationToken cancellationToken = default);

	/// <summary>Observes an accepted job; cancellation only detaches this observer.</summary>
	/// <param name="job">The accepted job, reusable by another observer.</param>
	/// <param name="cancellationToken">The observation cancellation token.</param>
	/// <returns>The terminal result; unknown outcomes and canceled observations carry the job handle in their exceptions.</returns>
	ValueTask<StorageOperationResult> WaitForCompletionAsync(StorageJobHandle job, CancellationToken cancellationToken = default);
}
