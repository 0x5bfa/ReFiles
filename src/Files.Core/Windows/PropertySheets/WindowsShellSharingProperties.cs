// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains SMB sharing state for a folder.
/// </summary>
public sealed class WindowsShellSharingProperties
{
	/// <summary>Gets the local folder path represented by the page.</summary>
	public string ObjectPath { get; }

	/// <summary>Gets the folder name displayed by the page.</summary>
	public string DisplayName { get; }

	/// <summary>Gets a value indicating whether the folder is inside an SMB share.</summary>
	public bool IsShared { get; }

	/// <summary>Gets the containing share name.</summary>
	public string ShareName { get; }

	/// <summary>Gets the UNC path that addresses the selected folder.</summary>
	public string NetworkPath { get; }

	/// <summary>Gets a value indicating whether password-protection guidance is applicable to this computer.</summary>
	public bool ShowPasswordProtection { get; }

	/// <summary>Gets a value indicating whether users must authenticate to access shared folders.</summary>
	public bool IsPasswordProtectionEnabled { get; }

	internal WindowsShellSharingProperties(string objectPath, string displayName, bool isShared, string shareName, string networkPath, bool showPasswordProtection, bool isPasswordProtectionEnabled)
	{
		ObjectPath = objectPath;
		DisplayName = displayName;
		IsShared = isShared;
		ShareName = shareName;
		NetworkPath = networkPath;
		ShowPasswordProtection = showPasswordProtection;
		IsPasswordProtectionEnabled = isPasswordProtectionEnabled;
	}
}
