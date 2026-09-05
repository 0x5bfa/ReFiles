// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Describes advanced filesystem attribute capabilities.
/// </summary>
public readonly record struct WindowsFileAttributeCapabilities
{
	/// <summary>Gets a value indicating whether per-file compression is supported.</summary>
	public bool SupportsCompression { get; }

	/// <summary>Gets a value indicating whether EFS encryption is supported.</summary>
	public bool SupportsEncryption { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="WindowsFileAttributeCapabilities"/> structure.
	/// </summary>
	/// <param name="supportsCompression">Whether per-file compression is supported.</param>
	/// <param name="supportsEncryption">Whether EFS encryption is supported.</param>
	public WindowsFileAttributeCapabilities(bool supportsCompression, bool supportsEncryption)
	{
		SupportsCompression = supportsCompression;
		SupportsEncryption = supportsEncryption;
	}
}
