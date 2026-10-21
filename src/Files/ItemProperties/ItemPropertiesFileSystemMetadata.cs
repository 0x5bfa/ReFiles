// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using Files.Core.Windows;
using Windows.Win32;

namespace Files.ItemProperties;

internal sealed record ItemPropertiesFileSystemMetadata(ulong Size, ulong SizeOnDisk, int FileCount, int FolderCount, bool HasDirectory, IReadOnlyList<DateTime> CreationTimes,
	IReadOnlyList<DateTime> ModifiedTimes, IReadOnlyList<DateTime> AccessedTimes, ItemPropertiesDriveMetadata? Drive);
