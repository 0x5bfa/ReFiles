// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;

namespace Files.Core.Storage;

/// <summary>
/// Specifies the kinds of storage items returned by folder enumeration.
/// </summary>
[Flags]
public enum StorableType : byte
{
	/// <summary>
	/// No item types.
	/// </summary>
	None = 0,

	/// <summary>
	/// Files.
	/// </summary>
	File = 1,

	/// <summary>
	/// Folders.
	/// </summary>
	Folder = 2,

	/// <summary>
	/// Files and folders.
	/// </summary>
	All = byte.MaxValue,
}
