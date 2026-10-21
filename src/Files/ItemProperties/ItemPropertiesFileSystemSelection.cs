// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using Files.Core.Windows;
using Windows.Win32;

namespace Files.ItemProperties;

internal sealed record ItemPropertiesFileSystemSelection(IReadOnlyList<string> Paths, bool HasFolders, bool IsSingleFile, bool IsSingleFolder, bool IsDrive, bool? IsReadOnly, bool? IsHidden,
	bool? IsArchive, bool? IsIndexed, bool? IsCompressed, bool? IsEncrypted, WindowsFileAttributeCapabilities Capabilities);
