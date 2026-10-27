// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.BackgroundServer;

/// <summary>Identifies the source value represented by a transfer descriptor.</summary>
public enum StorageTransferSourceKind
{
	/// <summary>An item in a configured source.</summary>
	ConfiguredItem,
	/// <summary>One physical record in an archive.</summary>
	ArchiveRecord,
	/// <summary>A logical folder in an archive.</summary>
	ArchiveFolder,
}
