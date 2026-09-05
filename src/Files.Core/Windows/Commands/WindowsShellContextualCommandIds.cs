// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Defines the stable identifiers used for contextual Windows Shell commands.
/// </summary>
public static class WindowsShellContextualCommandIds
{
	/// <summary>Mounts a disc image.</summary>
	public const string Mount = "windows.mount";

	/// <summary>Burns a disc image.</summary>
	public const string BurnDiscImage = "windows.discimage.burn";

	/// <summary>Sets the selected image as the desktop background.</summary>
	public const string SetDesktopBackground = "windows.setdesktopwallpaper";

	/// <summary>Empties the Recycle Bin.</summary>
	public const string EmptyRecycleBin = "windows.recyclebin.empty";

	/// <summary>Restores every item in the Recycle Bin.</summary>
	public const string RestoreAllRecycleBinItems = "windows.recyclebin.restoreall";

	/// <summary>Restores the selected Recycle Bin items.</summary>
	public const string RestoreRecycleBinItems = "windows.recyclebin.restoreitems";

	/// <summary>Creates a ZIP archive from the selection.</summary>
	public const string CompressToZip = "windows.zip.action";

	/// <summary>Pins a folder to Quick access.</summary>
	public const string PinToQuickAccess = "windows.pintohome";

	/// <summary>Adds a file to Favorites.</summary>
	public const string AddToFavorites = "windows.pintohomefile";

	/// <summary>Copies the selected item paths.</summary>
	public const string CopyAsPath = "windows.copyaspath";
}
