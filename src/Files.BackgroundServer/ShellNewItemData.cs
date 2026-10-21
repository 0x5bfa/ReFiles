// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.BackgroundServer;

/// <summary>Transfers one cached item from the Windows Shell New menu.</summary>
public struct ShellNewItemData
{
	/// <summary>The command offset used to invoke this item.</summary>
	public uint CommandOffset;
	/// <summary>The localized display name.</summary>
	public string Name;
	/// <summary>The encoded PNG icon as Base64 text.</summary>
	public string IconDataBase64;
	/// <summary>Whether the Shell enabled the item.</summary>
	public bool IsEnabled;
}
