// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Describes one item exposed by the Windows Shell New menu.
/// </summary>
public sealed class WindowsShellNewItem
{
	/// <summary>
	/// Gets the command offset used to invoke the item.
	/// </summary>
	public uint CommandOffset { get; }

	/// <summary>
	/// Gets the localized display name.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the encoded PNG icon data supplied by the Windows Shell.
	/// </summary>
	public ReadOnlyMemory<byte> IconData { get; }

	/// <summary>
	/// Gets a value indicating whether the Shell enabled the item.
	/// </summary>
	public bool IsEnabled { get; }

	/// <summary>
	/// Initializes a Shell New menu item.
	/// </summary>
	/// <param name="commandOffset">The command offset used by <c>IContextMenu::InvokeCommand</c>.</param>
	/// <param name="name">The localized display name.</param>
	/// <param name="iconData">The encoded PNG icon data.</param>
	/// <param name="isEnabled">A value indicating whether the Shell enabled the item.</param>
	internal WindowsShellNewItem(uint commandOffset, string name, ReadOnlyMemory<byte> iconData, bool isEnabled)
	{
		CommandOffset = commandOffset;
		Name = name;
		IconData = iconData;
		IsEnabled = isEnabled;
	}
}
