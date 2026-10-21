// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using Files.Core.Storage;

namespace Files.StorageOperations;

internal enum TrackedStorageOperationState
{
	Running,
	Pausing,
	Paused,
	Resuming,
	WaitingForUser,
	Succeeded,
	Failed,
	Canceled,
}
