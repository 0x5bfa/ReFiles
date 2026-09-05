// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains the Shell identity and optional free-threaded item-store reference needed to materialize a Windows Shell item.
/// </summary>
internal sealed record WindowsItemLocator
{
	public ReadOnlyMemory<byte> AbsolutePidl { get; }

	public WindowsItemLocator? ParentFolder { get; }

	public string ParsingName { get; }

	public ReadOnlyMemory<byte> RelativePidl { get; }

	internal WindowsShellItemStoreReference? ItemStoreReference { get; }

	public WindowsItemLocator(ReadOnlyMemory<byte> absolutePidl, string parsingName, WindowsItemLocator? parentFolder = null, ReadOnlyMemory<byte> relativePidl = default, WindowsShellItemStoreReference? itemStoreReference = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		if ((parentFolder is null) != relativePidl.IsEmpty)
		{
			throw new ArgumentException("A relative PIDL requires a parent folder locator.", nameof(relativePidl));
		}

		if (itemStoreReference is not null && (parentFolder is null || relativePidl.IsEmpty))
		{
			throw new ArgumentException("An item-store reference requires a parent folder and relative PIDL.", nameof(itemStoreReference));
		}

		AbsolutePidl = absolutePidl;
		ParentFolder = parentFolder;
		ParsingName = parsingName;
		RelativePidl = relativePidl;
		ItemStoreReference = itemStoreReference;
	}
}
