// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.StorageOperations;

internal interface IOperationStatusSource : IStorageOperationMonitor
{
	IReadOnlyList<StorageOperationStatus> GetOperations();

	IReadOnlyList<StorageOperationState> IStorageOperationMonitor.GetOperationStates()
	{
		return GetOperations().Select(StorageOperationStatusMapper.ToState).ToArray();
	}
}
