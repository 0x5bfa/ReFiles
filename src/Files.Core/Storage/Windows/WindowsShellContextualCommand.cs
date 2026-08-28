// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>
/// Defines the stable identifiers used for contextual Windows Shell commands.
/// </summary>
public static class WindowsShellContextualCommandIds
{
	/// <summary>Mounts a disc image.</summary>
	public const string Mount = "windows.mount";

	/// <summary>Burns a disc image.</summary>
	public const string BurnDiscImage = "windows.discimage.burn";

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

/// <summary>
/// Describes one contextual Windows Shell command without retaining apartment-bound COM objects.
/// </summary>
public sealed class WindowsShellContextualCommand
{
	internal WindowsShellContextualCommandToken Token { get; }

	/// <summary>Gets the stable command identifier.</summary>
	public string Id { get; }

	/// <summary>Gets a value indicating whether the command can currently be invoked.</summary>
	public bool IsEnabled { get; }

	internal WindowsShellContextualCommand(string id, bool isEnabled, WindowsShellContextualCommandToken token)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		ArgumentNullException.ThrowIfNull(token);

		Id = id;
		IsEnabled = isEnabled;
		Token = token;
	}
}

internal abstract record WindowsShellContextualCommandToken;

internal sealed record WindowsShellAppExtensionContextualCommandToken(WindowsShellAppExtensionCommand Command) : WindowsShellContextualCommandToken;

internal sealed record WindowsShellContextMenuContextualCommandToken(WindowsShellContextMenuTargetKind TargetKind) : WindowsShellContextualCommandToken;

internal sealed record WindowsShellEmptyRecycleBinContextualCommandToken : WindowsShellContextualCommandToken;

internal enum WindowsShellContextMenuTargetKind
{
	Selection,
	LocationItem,
	LocationBackground,
}
