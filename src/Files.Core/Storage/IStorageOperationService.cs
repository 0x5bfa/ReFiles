// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Selects a storage operation handler and executes a request.
/// </summary>
public interface IStorageOperationService
{
	/// <summary>Determines whether a registered handler supports a request.</summary>
	/// <param name="request">The operation request.</param>
	/// <returns><see langword="true"/> when a handler can execute the request.</returns>
	bool CanHandle(StorageOperationRequest request);

	/// <summary>Executes a request through a registered handler.</summary>
	/// <param name="request">The operation request.</param>
	/// <param name="progress">The optional progress receiver.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The operation result.</returns>
	ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default);
}
