// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>
/// Contains only apartment-neutral data needed to materialize a Windows Shell item.
/// </summary>
internal sealed record WindowsItemLocator
{
	public ReadOnlyMemory<byte> AbsolutePidl { get; }

	public string ParsingName { get; }

	public WindowsItemLocator(ReadOnlyMemory<byte> absolutePidl, string parsingName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		AbsolutePidl = absolutePidl;
		ParsingName = parsingName;
	}
}
