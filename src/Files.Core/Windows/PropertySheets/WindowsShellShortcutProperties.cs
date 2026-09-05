// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains values read from an <c>IShellLinkW</c> object.
/// </summary>
public sealed class WindowsShellShortcutProperties
{
	/// <summary>Gets the shortcut target path.</summary>
	public string TargetPath { get; }

	/// <summary>Gets the target's Shell type description.</summary>
	public string TargetType { get; }

	/// <summary>Gets the target's containing folder name.</summary>
	public string TargetLocation { get; }

	/// <summary>Gets the command-line arguments.</summary>
	public string Arguments { get; }

	/// <summary>Gets the working directory.</summary>
	public string WorkingDirectory { get; }

	/// <summary>Gets the encoded Shell hotkey.</summary>
	public ushort Hotkey { get; }

	/// <summary>Gets the target window show command.</summary>
	public int ShowCommand { get; }

	/// <summary>Gets the shortcut comment.</summary>
	public string Comment { get; }

	/// <summary>Gets the custom icon path.</summary>
	public string IconPath { get; }

	/// <summary>Gets the custom icon resource index.</summary>
	public int IconIndex { get; }

	internal WindowsShellShortcutProperties(
		string targetPath,
		string targetType,
		string targetLocation,
		string arguments,
		string workingDirectory,
		ushort hotkey,
		int showCommand,
		string comment,
		string iconPath,
		int iconIndex)
	{
		TargetPath = targetPath;
		TargetType = targetType;
		TargetLocation = targetLocation;
		Arguments = arguments;
		WorkingDirectory = workingDirectory;
		Hotkey = hotkey;
		ShowCommand = showCommand;
		Comment = comment;
		IconPath = iconPath;
		IconIndex = iconIndex;
	}
}
