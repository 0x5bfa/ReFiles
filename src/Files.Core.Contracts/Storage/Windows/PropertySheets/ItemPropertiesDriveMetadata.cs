// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>Contains immutable filesystem property data.</summary>
/// <param name="VolumeLabel">The unformatted volume label.</param>
/// <param name="FileSystem">The filesystem name.</param>
/// <param name="UsedSpace">The used volume capacity in bytes.</param>
/// <param name="FreeSpace">The free volume capacity in bytes.</param>
/// <param name="Capacity">The total volume capacity in bytes.</param>
/// <param name="IsCompressed">The compressed state, or null for a mixed selection.</param>
/// <param name="IsIndexed">The indexed state, or null for a mixed selection.</param>
/// <param name="SupportsCompression">Whether filesystem compression is supported.</param>
/// <param name="SupportsIndexing">Whether volume indexing can be configured.</param>
/// <param name="CanRename">Whether the volume label can be changed.</param>
/// <param name="SupportsStorageDetails">Whether Windows Storage settings can display the volume.</param>
public sealed record ItemPropertiesDriveMetadata(string VolumeLabel, string FileSystem, ulong UsedSpace, ulong FreeSpace, ulong Capacity, bool IsCompressed, bool IsIndexed, bool SupportsCompression,
	bool SupportsIndexing, bool CanRename, bool SupportsStorageDetails);
