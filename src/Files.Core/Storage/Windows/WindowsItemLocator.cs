// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>
/// Contains only apartment-neutral data needed to materialize a Windows Shell item.
/// </summary>
internal sealed record WindowsItemLocator
{
	public ReadOnlyMemory<byte> AbsolutePidl { get; }

	public WindowsItemLocator? ParentFolder { get; }

	public string ParsingName { get; }

	public ReadOnlyMemory<byte> RelativePidl { get; }

	public WindowsItemLocator(ReadOnlyMemory<byte> absolutePidl, string parsingName, WindowsItemLocator? parentFolder = null, ReadOnlyMemory<byte> relativePidl = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		if ((parentFolder is null) != relativePidl.IsEmpty)
		{
			throw new ArgumentException("A relative PIDL requires a parent folder locator.", nameof(relativePidl));
		}

		AbsolutePidl = absolutePidl;
		ParentFolder = parentFolder;
		ParsingName = parsingName;
		RelativePidl = relativePidl;
	}
}
