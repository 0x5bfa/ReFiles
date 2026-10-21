// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Controls;

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
