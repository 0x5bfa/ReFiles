// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Windows;

/// <summary>
/// Describes an OwlCore item backed by the Windows Shell namespace.
/// </summary>
public interface IWindowsStorable : IStorableChild, IStorageAddressSource
{
	/// <summary>Gets the Windows Shell parsing name.</summary>
	string ParsingName { get; }

	/// <summary>Gets the file-system path, when the item has one.</summary>
	string? FileSystemPath { get; }

	/// <summary>Gets a value indicating whether the item is file-system backed.</summary>
	bool IsFileSystem { get; }

	/// <summary>Gets a value indicating whether the item is exposed as a stream.</summary>
	bool IsStream { get; }

	/// <summary>
	/// Gets a value indicating whether the Shell marks the item as hidden.
	/// </summary>
	bool IsHidden => false;
}
