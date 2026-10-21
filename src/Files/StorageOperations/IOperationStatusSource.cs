// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.StorageOperations;

internal interface IOperationStatusSource
{
	event EventHandler? Changed;
	IReadOnlyList<StorageOperationStatus> GetOperations();
	bool Cancel(Guid id);
	bool SetPaused(Guid id, bool paused);
	bool ResolveInterruption(Guid id, Guid interruptionId, StorageOperationInterruptionResponse response);
	bool Forget(Guid id);
}
