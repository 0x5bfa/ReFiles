// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Changes;

/// <summary>
/// Describes the kind of change reported for a folder.
/// </summary>
public enum FolderChangeKind
{
	Created,
	Deleted,
	Renamed,
	Updated,
	DirectoryUpdated,
}
