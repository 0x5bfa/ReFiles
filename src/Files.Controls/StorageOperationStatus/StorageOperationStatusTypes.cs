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
}

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

/// <summary>Specifies an action raised by a storage operation status item.</summary>
public enum StorageOperationStatusAction
{
	/// <summary>Expand the operation details.</summary>
	Expand,
	/// <summary>Collapse the operation details.</summary>
	Collapse,
	/// <summary>Pause the operation.</summary>
	Pause,
	/// <summary>Resume the operation.</summary>
	Resume,
	/// <summary>Retry the current item.</summary>
	Retry,
	/// <summary>Skip the current item.</summary>
	Skip,
	/// <summary>Continue with administrator rights.</summary>
	Continue,
	/// <summary>Confirm the requested action.</summary>
	Yes,
	/// <summary>Reject the requested action.</summary>
	No,
	/// <summary>Delete the conflicting item.</summary>
	Delete,
	/// <summary>Acknowledge the interruption.</summary>
	Ok,
	/// <summary>Cancel the operation.</summary>
	Cancel,
	/// <summary>Remove the status item.</summary>
	Remove,
}

/// <summary>Provides data for a storage operation status item action.</summary>
public sealed class StorageOperationStatusActionEventArgs : EventArgs
{
	/// <summary>Gets the requested action.</summary>
	public StorageOperationStatusAction Action { get; }

	/// <summary>Gets a value indicating whether the decision applies to remaining matching interruptions.</summary>
	public bool ApplyToAll { get; }

	/// <summary>Initializes storage operation status action data.</summary>
	/// <param name="action">The requested action.</param>
	/// <param name="applyToAll">Whether the decision applies to remaining matching interruptions.</param>
	public StorageOperationStatusActionEventArgs(StorageOperationStatusAction action, bool applyToAll = false)
	{
		Action = action;
		ApplyToAll = applyToAll;
	}
}
