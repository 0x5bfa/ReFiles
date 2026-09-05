// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

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
