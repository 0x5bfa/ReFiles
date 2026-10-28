// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Provides an explicit submit-and-observe workflow for callers that need a terminal result.</summary>
public static class StorageOperationExecution
{
	/// <summary>Executes locally or submits and observes an independent job.</summary>
	/// <param name="service">The selected execution boundary.</param>
	/// <param name="requests">The complete operation intent.</param>
	/// <param name="cancellationToken">Cancels local execution, or preparation and observation of a remote job.</param>
	/// <returns>The terminal result. Detached observations retain the accepted identity in their exception.</returns>
	public static async ValueTask<StorageOperationResult> ExecuteAndWaitAsync(this IStorageOperationService service, IReadOnlyList<StorageOperationRequest> requests,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(service);

		ArgumentNullException.ThrowIfNull(requests);

		if (service.Jobs is not { } jobs)
		{
			return await service.ExecuteBatchAsync(requests, cancellationToken).ConfigureAwait(false);
		}

		if (requests.Count is 0)
		{
			return new StorageOperationResult(true, null);
		}

		var job = await jobs.SubmitBatchAsync(Guid.NewGuid(), requests, cancellationToken).ConfigureAwait(false);

		try
		{
			return await jobs.WaitForCompletionAsync(job, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested && error is not StorageJobObservationCanceledException)
		{
			throw new StorageJobObservationCanceledException(job, cancellationToken);
		}
	}
}
