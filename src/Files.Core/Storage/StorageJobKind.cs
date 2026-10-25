// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Describes an operation kind.</summary>
public enum StorageJobKind
{
	/// <summary>Copies items.</summary>
	Copy,
	/// <summary>Moves items.</summary>
	Move,
	/// <summary>Deletes items.</summary>
	Delete,
	/// <summary>Executes another operation kind.</summary>
	Other,
}
