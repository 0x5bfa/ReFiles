// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Changes;

/// <summary>
/// Describes the kind of change reported for a folder.
/// </summary>
public enum FolderChangeKind
{
	/// <summary>An item was created.</summary>
	Created,
	/// <summary>An item was deleted.</summary>
	Deleted,
	/// <summary>An item was renamed.</summary>
	Renamed,
	/// <summary>An item's metadata or contents were updated.</summary>
	Updated,
	/// <summary>A directory's contents changed.</summary>
	DirectoryUpdated,
}
