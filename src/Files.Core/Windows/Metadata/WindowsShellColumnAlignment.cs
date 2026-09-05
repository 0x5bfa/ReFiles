// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Describes the alignment used by a Windows Shell details column.
/// </summary>
public enum WindowsShellColumnAlignment
{
	/// <summary>Aligns values to the left.</summary>
	Left,

	/// <summary>Aligns values to the right.</summary>
	Right,

	/// <summary>Centers values.</summary>
	Center,
}
