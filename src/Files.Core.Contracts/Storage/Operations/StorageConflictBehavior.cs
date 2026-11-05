// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Specifies how an operation handles an existing destination name.</summary>
public enum StorageConflictBehavior
{
	/// <summary>Fail when the destination name already exists.</summary>
	Fail,
	/// <summary>Generate a unique destination name.</summary>
	GenerateUniqueName,
	/// <summary>Request a user decision when the destination name already exists.</summary>
	Prompt,
}
