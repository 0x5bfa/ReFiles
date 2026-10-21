// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Controls;

/// <summary>Specifies the operation represented by a storage operation status item.</summary>
public enum StorageOperationStatusKind
{
	/// <summary>A copy operation.</summary>
	Copy,
	/// <summary>A move operation.</summary>
	Move,
	/// <summary>A delete operation.</summary>
	Delete,
	/// <summary>Another storage operation.</summary>
	Other,
}
