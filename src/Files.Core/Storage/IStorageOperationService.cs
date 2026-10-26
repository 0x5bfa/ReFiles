// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Selects a storage operation handler and executes a request.
/// </summary>
public interface IStorageOperationService
{
	/// <summary>Gets the optional monitor for accepted operations.</summary>
	IStorageOperationMonitor? Monitor => null;

	/// <summary>Gets the optional independent job execution boundary. Use it instead of inline execution when present.</summary>
	IStorageJobService? Jobs => null;

	/// <summary>Determines whether a registered handler supports a request.</summary>
	/// <param name="request">The operation request.</param>
	/// <returns><see langword="true"/> when a handler can execute the request.</returns>
	bool CanHandle(StorageOperationRequest request);

	/// <summary>Executes a request through a registered handler.</summary>
	/// <param name="request">The operation request.</param>
	/// <param name="progress">The optional progress receiver.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="operationControl">The optional cooperative operation control.</param>
	/// <returns>The operation result.</returns>
	ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default,
		IStorageOperationControl? operationControl = null);

	/// <summary>Executes a complete batch through the selected execution boundary.</summary>
	/// <param name="requests">The complete, immutable operation intent.</param>
	/// <param name="cancellationToken">The token used to cancel inline execution.</param>
	/// <returns>The last successful result or the first failure. Local execution stops on failure.</returns>
	async ValueTask<StorageOperationResult> ExecuteBatchAsync(IReadOnlyList<StorageOperationRequest> requests, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(requests);

		var snapshot = requests.ToArray();
		if (snapshot.Any(static request => request is null))
		{
			throw new ArgumentException("Operation batches cannot contain null requests.", nameof(requests));
		}

		cancellationToken.ThrowIfCancellationRequested();

		if (snapshot.Any(request => !CanHandle(request)))
		{
			return new StorageOperationResult(false, null, new NotSupportedException("The batch contains an unsupported operation."));
		}

		var result = new StorageOperationResult(true, null);
		foreach (var request in snapshot)
		{
			result = await ExecuteAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
			if (!result.Succeeded)
			{
				return result;
			}
		}

		return result;
	}
}
