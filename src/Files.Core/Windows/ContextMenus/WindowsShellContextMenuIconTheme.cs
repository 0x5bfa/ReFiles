// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>Selects the Windows context-menu asset contrast and theme.</summary>
public enum WindowsShellContextMenuIconTheme
{
	/// <summary>Uses standard contrast with a light background.</summary>
	Light,
	/// <summary>Uses standard contrast with a dark background.</summary>
	Dark,
	/// <summary>Uses high contrast with a light background.</summary>
	HighContrastWhite,
	/// <summary>Uses high contrast with a dark background.</summary>
	HighContrastBlack,
}
