// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Operations;

/// <summary>Identifies a supported storage command.</summary>
public enum StorageOperationKind
{
	Copy,
	Move,
	Delete,
	Rename,
	CreateFile,
	CreateFolder,
}
