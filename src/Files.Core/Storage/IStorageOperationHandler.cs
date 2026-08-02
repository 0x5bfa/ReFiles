// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Executes storage operations for one storage backend.
/// </summary>
public interface IStorageOperationHandler
{
	bool CanHandle(StorageOperationRequest request);

	ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default);
}
