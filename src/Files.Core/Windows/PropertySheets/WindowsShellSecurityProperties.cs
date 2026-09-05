// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains NTFS discretionary access-control state for one file-system item.
/// </summary>
public sealed class WindowsShellSecurityProperties
{
	/// <summary>Gets the path whose access-control list was read.</summary>
	public string ObjectPath { get; }

	/// <summary>Gets the principals present in the discretionary access-control list.</summary>
	public IReadOnlyList<WindowsShellSecurityPrincipal> Principals { get; }

	internal WindowsShellSecurityProperties(string objectPath, IReadOnlyList<WindowsShellSecurityPrincipal> principals)
	{
		ObjectPath = objectPath;
		Principals = principals;
	}
}
