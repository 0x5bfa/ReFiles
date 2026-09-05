// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Describes the display type suggested by a Windows Shell column.
/// </summary>
public enum WindowsShellColumnType
{
	/// <summary>The Shell did not specify a display type.</summary>
	Default,

	/// <summary>The value is displayed as text.</summary>
	String,

	/// <summary>The value is displayed as an integer.</summary>
	Integer,

	/// <summary>The value is displayed as a date and time.</summary>
	DateTime,
}
