// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using Files.Core.Windows;
using Windows.Win32;

namespace Files.ItemProperties;

internal sealed record ItemPropertiesFileSystemChanges(bool IsDrive, bool InitialIsDriveCompressed, bool InitialIsDriveIndexed, bool IsDriveCompressed, bool IsDriveIndexed,
	bool? IsReadOnly, bool? IsHidden, bool? IsArchive, bool? IsIndexed, bool? IsCompressed, bool? IsEncrypted, bool UpdateCompression, bool UpdateEncryption, bool ApplyToContents);
