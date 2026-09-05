// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Describes the default command exposed by a Windows Shell item's context menu.
/// </summary>
public sealed class WindowsShellDefaultCommand
{
	/// <summary>
	/// Gets the language-independent command verb, when the context-menu provider exposes one.
	/// </summary>
	public string? CanonicalVerb { get; }

	internal WindowsShellDefaultCommand(string? canonicalVerb)
	{
		CanonicalVerb = canonicalVerb;
	}
}
