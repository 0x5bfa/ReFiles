// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Storage;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Describes a Files storage item backed by the Windows Shell namespace.
/// </summary>
public interface IWindowsStorable : IStorableChild, IStorageAddressSource, IStorageItemAttributes
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
	new bool IsHidden => false;

	bool IStorageItemAttributes.IsHidden => IsHidden;
}
