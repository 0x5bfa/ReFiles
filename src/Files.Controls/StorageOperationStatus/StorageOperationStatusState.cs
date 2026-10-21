// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Controls;

/// <summary>Specifies the visual state of a storage operation status item.</summary>
public enum StorageOperationStatusState
{
	/// <summary>The operation is running.</summary>
	Running,
	/// <summary>The operation is waiting for its backend to pause.</summary>
	Pausing,
	/// <summary>The operation is paused.</summary>
	Paused,
	/// <summary>The operation is waiting for its backend to resume.</summary>
	Resuming,
	/// <summary>The operation is waiting for a user decision.</summary>
	WaitingForUser,
	/// <summary>The operation succeeded.</summary>
	Succeeded,
	/// <summary>The operation failed.</summary>
	Failed,
	/// <summary>The operation was canceled.</summary>
	Canceled,
}
