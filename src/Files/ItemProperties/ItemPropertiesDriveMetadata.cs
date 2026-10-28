// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using Files.Core.Storage.Windows;
using Windows.Win32;

namespace Files.ItemProperties;

internal sealed record ItemPropertiesDriveMetadata(string VolumeLabel, string FileSystem, ulong UsedSpace, ulong FreeSpace, ulong Capacity, bool IsCompressed, bool IsIndexed, bool SupportsCompression,
	bool SupportsIndexing, bool CanRename, bool SupportsStorageDetails);
