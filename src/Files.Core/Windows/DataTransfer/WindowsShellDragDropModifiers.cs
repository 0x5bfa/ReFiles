// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Identifies pointer buttons and modifier keys active during a Windows Shell drag operation.
/// </summary>
[Flags]
public enum WindowsShellDragDropModifiers : uint
{
	/// <summary>No pointer button or modifier key is active.</summary>
	None = 0,

	/// <summary>The left pointer button is pressed.</summary>
	LeftButton = 0x0001,

	/// <summary>The right pointer button is pressed.</summary>
	RightButton = 0x0002,

	/// <summary>The Shift key is pressed.</summary>
	Shift = 0x0004,

	/// <summary>The Control key is pressed.</summary>
	Control = 0x0008,

	/// <summary>The middle pointer button is pressed.</summary>
	MiddleButton = 0x0010,

	/// <summary>The Alt key is pressed.</summary>
	Alt = 0x0020,
}
