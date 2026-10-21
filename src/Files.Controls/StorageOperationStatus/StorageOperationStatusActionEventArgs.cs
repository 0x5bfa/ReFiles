// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Controls;

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
