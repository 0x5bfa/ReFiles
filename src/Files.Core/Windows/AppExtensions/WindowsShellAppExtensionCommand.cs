// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Describes an app-provided File Explorer command without retaining apartment-bound COM objects.
/// </summary>
public sealed class WindowsShellAppExtensionCommand
{
	internal WindowsShellAppExtensionCommandToken Token { get; }

	/// <summary>Gets the stable manifest verb identifier.</summary>
	public string Id { get; }

	/// <summary>Gets the text displayed for the command.</summary>
	public string Title { get; }

	/// <summary>Gets the optional Shell icon resource path.</summary>
	public string? IconPath { get; }

	/// <summary>Gets the icon resource index within <see cref="IconPath"/>.</summary>
	public int IconIndex { get; }

	/// <summary>Gets a value indicating whether the command can be invoked.</summary>
	public bool IsEnabled { get; }

	/// <summary>Gets a value indicating whether the command is checked.</summary>
	public bool IsChecked { get; }

	/// <summary>Gets a value indicating whether the command uses radio-check presentation.</summary>
	public bool IsRadio { get; }

	/// <summary>Gets a value indicating whether the command represents a separator.</summary>
	public bool IsSeparator { get; }

	/// <summary>Gets the child commands supplied by <c>IExplorerCommand::EnumSubCommands</c>.</summary>
	public IReadOnlyList<WindowsShellAppExtensionCommand> Children { get; }

	internal WindowsShellAppExtensionCommand(
		WindowsShellAppExtensionCommandToken token,
		string id,
		string title,
		string? iconPath,
		int iconIndex,
		bool isEnabled,
		bool isChecked,
		bool isRadio,
		bool isSeparator,
		IReadOnlyList<WindowsShellAppExtensionCommand>? children = null)
	{
		ArgumentNullException.ThrowIfNull(token);
		ArgumentNullException.ThrowIfNull(id);
		ArgumentNullException.ThrowIfNull(title);

		Token = token;
		Id = id;
		Title = title;
		IconPath = iconPath;
		IconIndex = iconIndex;
		IsEnabled = isEnabled;
		IsChecked = isChecked;
		IsRadio = isRadio;
		IsSeparator = isSeparator;
		Children = children ?? [];
	}
}
