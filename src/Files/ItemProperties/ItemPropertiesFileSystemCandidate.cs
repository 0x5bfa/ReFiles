// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using Files.Core.Windows;
using Windows.Win32;

namespace Files.ItemProperties;

internal sealed record ItemPropertiesFileSystemCandidate(string Path, bool IsFolder);
