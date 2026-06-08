// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Selects a storage operation handler and executes a request.
/// </summary>
public interface IStorageOperationService
{
	bool CanHandle(StorageOperationRequest request);

	ValueTask<StorageOperationResult> ExecuteAsync(
		StorageOperationRequest request,
		IProgress<StorageOperationProgress>? progress = null,
		CancellationToken cancellationToken = default);
}
