// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Operations;

/// <summary>Transfers one item of a complete storage operation batch.</summary>
public struct StorageOperationData
{
	public StorageOperationKind Kind;
	public StorageReferenceData Item;
	public StorageReferenceData Destination;
	public string Name;
	public string DisplayName;
	public int ConflictBehavior;
	public bool Permanently;
}
