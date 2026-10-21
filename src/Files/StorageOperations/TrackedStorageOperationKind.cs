// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using Files.Core.Storage;

namespace Files.StorageOperations;

internal enum TrackedStorageOperationKind
{
	Copy,
	Move,
	Delete,
	Other,
}
