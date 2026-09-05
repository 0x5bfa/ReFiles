// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains the executable resolved for Explorer's application compatibility page.
/// </summary>
public sealed class WindowsShellCompatibilityProperties
{
	/// <summary>Gets the executable path whose compatibility settings are managed.</summary>
	public string ExecutablePath { get; }

	internal WindowsShellCompatibilityProperties(string executablePath)
	{
		ExecutablePath = executablePath;
	}
}
