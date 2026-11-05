// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>Contains immutable filesystem property data.</summary>
/// <param name="Size">The total logical size in bytes.</param>
/// <param name="SizeOnDisk">The total allocated size in bytes.</param>
/// <param name="FileCount">The number of counted files.</param>
/// <param name="FolderCount">The number of counted subfolders.</param>
/// <param name="HasDirectory">Whether the selection includes a directory.</param>
/// <param name="CreationTimes">The selected items' local creation times.</param>
/// <param name="ModifiedTimes">The selected items' local modification times.</param>
/// <param name="AccessedTimes">The selected items' local last-access times.</param>
/// <param name="Drive">The volume metadata when a drive root is selected.</param>
public sealed record ItemPropertiesFileSystemMetadata(ulong Size, ulong SizeOnDisk, int FileCount, int FolderCount, bool HasDirectory, IReadOnlyList<DateTime> CreationTimes,
	IReadOnlyList<DateTime> ModifiedTimes, IReadOnlyList<DateTime> AccessedTimes, ItemPropertiesDriveMetadata? Drive);
