// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains folder customization values read through the Shell customization API.
/// </summary>
public sealed class WindowsShellFolderCustomizationProperties
{
	/// <summary>Gets the customized folder path.</summary>
	public string ObjectPath { get; }

	/// <summary>Gets the folder-kind canonical name.</summary>
	public string FolderKind { get; }

	/// <summary>Gets the folder picture path.</summary>
	public string PicturePath { get; }

	/// <summary>Gets the custom folder icon path.</summary>
	public string IconPath { get; }

	/// <summary>Gets the custom folder icon resource index.</summary>
	public int IconIndex { get; }

	/// <summary>Gets a value indicating whether Explorer exposes folder-picture customization for this folder.</summary>
	public bool CanChangePicture { get; }

	/// <summary>Gets a value indicating whether Explorer exposes folder-icon customization for this folder.</summary>
	public bool CanChangeIcon { get; }

	/// <summary>Gets a value indicating whether the current template is also stored in Explorer's inherited view-state bag.</summary>
	public bool ApplyToSubfolders { get; }

	internal WindowsShellFolderCustomizationProperties(string objectPath, string folderKind, string picturePath, string iconPath, int iconIndex, bool canChangePicture, bool canChangeIcon,
		bool applyToSubfolders)
	{
		ObjectPath = objectPath;
		FolderKind = folderKind;
		PicturePath = picturePath;
		IconPath = iconPath;
		IconIndex = iconIndex;
		CanChangePicture = canChangePicture;
		CanChangeIcon = canChangeIcon;
		ApplyToSubfolders = applyToSubfolders;
	}
}
