// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Controls;

/// <summary>Specifies the interruption response buttons shown by a storage operation status item.</summary>
[Flags]
public enum StorageOperationStatusActions
{
	/// <summary>No response button is shown.</summary>
	None = 0,
	/// <summary>Show the Try Again button.</summary>
	Retry = 1 << 0,
	/// <summary>Show the Skip button.</summary>
	Skip = 1 << 1,
	/// <summary>Show the Cancel button.</summary>
	Cancel = 1 << 2,
	/// <summary>Show the elevated Continue button.</summary>
	Continue = 1 << 3,
	/// <summary>Show the Yes button.</summary>
	Yes = 1 << 4,
	/// <summary>Show the No button.</summary>
	No = 1 << 5,
	/// <summary>Show the Delete button.</summary>
	Delete = 1 << 6,
	/// <summary>Show the OK button.</summary>
	Ok = 1 << 7,
}
