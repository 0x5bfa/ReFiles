// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.BackgroundServer;

/// <summary>Transfers a source selection without exporting live storage objects.</summary>
public struct StorageTransferData
{
	public StorageOperationKind Kind;
	public StorageTransferSourceKind SourceKind;
	public StorageReferenceData Root;
	public string RecordId;
	public string EntryPath;
	public int ArchiveStepOffset;
	public int ArchiveStepCount;
	public StorageReferenceData Destination;
	public string Name;
	public string DisplayName;
	public int ConflictBehavior;
}
