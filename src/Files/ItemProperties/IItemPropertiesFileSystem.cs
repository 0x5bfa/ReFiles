// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using Files.Core.Storage.Windows;
using Windows.Win32;

namespace Files.ItemProperties;

internal interface IItemPropertiesFileSystem
{
	ItemPropertiesFileSystemSelection Inspect(IReadOnlyList<ItemPropertiesFileSystemCandidate> candidates);

	ItemPropertiesFileSystemMetadata ReadMetadata(IReadOnlyList<string> paths, CancellationToken cancellationToken);

	void Apply(IReadOnlyList<string> paths, ItemPropertiesFileSystemChanges changes, CancellationToken cancellationToken);
}
