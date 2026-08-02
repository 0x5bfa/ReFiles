// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

public sealed class UnsupportedArchiveException : Exception
{
	public string DisplayName { get; }

	public UnsupportedArchiveException(string displayName)
		: base($"No archive backend can open '{displayName}'.")
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

		DisplayName = displayName;
	}
}
