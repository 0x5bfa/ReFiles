// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>
/// Creates source-specific identities independently from Shell addresses.
/// </summary>
internal interface IWindowsItemIdReader
{
	string GetItemId(string parsingName, string? fileSystemPath);

	bool TryGetParsingName(string itemId, out string parsingName);

	bool IsFileSystemIdentity(string itemId);
}
