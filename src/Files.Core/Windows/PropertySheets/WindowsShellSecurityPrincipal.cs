// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Describes the access masks assigned to a security principal.
/// </summary>
public sealed class WindowsShellSecurityPrincipal
{
	/// <summary>Gets the account's display name.</summary>
	public string Name { get; }

	/// <summary>Gets the account's string SID.</summary>
	public string Sid { get; }

	/// <summary>Gets the PNG data for the ACLUI principal icon.</summary>
	public ReadOnlyMemory<byte> IconData { get; }

	/// <summary>Gets the image index retained for compatibility with the ACLUI image-list source.</summary>
	public int IconIndex { get; }

	/// <summary>Gets the combined allowed access mask.</summary>
	public uint AllowedAccessMask { get; }

	/// <summary>Gets the combined denied access mask.</summary>
	public uint DeniedAccessMask { get; }

	internal WindowsShellSecurityPrincipal(string name, string sid, ReadOnlyMemory<byte> iconData, int iconIndex, uint allowedAccessMask, uint deniedAccessMask)
	{
		Name = name;
		Sid = sid;
		IconData = iconData;
		IconIndex = iconIndex;
		AllowedAccessMask = allowedAccessMask;
		DeniedAccessMask = deniedAccessMask;
	}
}
