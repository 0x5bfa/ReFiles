// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Executes storage operations for one storage backend.
/// </summary>
public interface IStorageOperationHandler
{
	/// <summary>Determines whether this handler supports a request.</summary>
	/// <param name="request">The operation request.</param>
	/// <returns><see langword="true"/> when this handler can execute the request.</returns>
	bool CanHandle(StorageOperationRequest request);

	/// <summary>Executes a storage operation.</summary>
	/// <param name="request">The operation request.</param>
	/// <param name="progress">The optional progress receiver.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="operationControl">The optional cooperative operation control.</param>
	/// <returns>The operation result.</returns>
	ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default,
		IStorageOperationControl? operationControl = null);
}
