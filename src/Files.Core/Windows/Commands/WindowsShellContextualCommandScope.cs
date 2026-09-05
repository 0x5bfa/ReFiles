// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Identifies the browsing context whose changes can affect a contextual Windows Shell command.
/// </summary>
[Flags]
public enum WindowsShellContextualCommandScope
{
	/// <summary>The command is not affected by the browsing context.</summary>
	None = 0,

	/// <summary>The command depends on the selected items.</summary>
	Selection = 1 << 0,

	/// <summary>The command depends on the current location.</summary>
	Location = 1 << 1,

	/// <summary>The command can depend on the selection or current location.</summary>
	All = Selection | Location,
}
