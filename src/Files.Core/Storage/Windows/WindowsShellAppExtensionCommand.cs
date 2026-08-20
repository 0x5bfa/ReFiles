// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

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
		IsEnabled = isEnabled;
		IsChecked = isChecked;
		IsRadio = isRadio;
		IsSeparator = isSeparator;
		Children = children ?? [];
	}
}

internal sealed record WindowsShellAppExtensionCommandToken(Guid ClassId, string VerbId, IReadOnlyList<int> SubCommandPath);
