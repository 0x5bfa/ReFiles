// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>
/// Identifies operations supported by a Windows Shell drag source or drop target.
/// </summary>
[Flags]
public enum WindowsShellDropEffects : uint
{
	/// <summary>No operation is supported.</summary>
	None = 0,

	/// <summary>The data can be copied.</summary>
	Copy = 1,

	/// <summary>The data can be moved.</summary>
	Move = 2,

	/// <summary>A link to the data can be created.</summary>
	Link = 4,
}

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
