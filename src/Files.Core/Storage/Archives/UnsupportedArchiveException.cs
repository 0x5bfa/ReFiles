// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Indicates that no archive backend supports an archive.</summary>
public sealed class UnsupportedArchiveException : Exception
{
	/// <summary>Gets the archive display name.</summary>
	public string DisplayName { get; }

	/// <summary>Initializes an unsupported archive exception.</summary>
	/// <param name="displayName">The archive display name.</param>
	public UnsupportedArchiveException(string displayName)
		: base($"No archive backend can open '{displayName}'.")
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

		DisplayName = displayName;
	}
}
