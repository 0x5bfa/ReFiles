// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Text;

namespace Files.Core.Storage.Windows;

/// <summary>Describes a context-menu entry without exposing native handles or COM objects.</summary>
public sealed class WindowsShellContextMenuEntry
{
	internal WindowsShellContextMenuIcon? Icon { get; }

	/// <summary>Gets the command identity supplied by its application or Shell source.</summary>
	public string Id { get; }

	/// <summary>Gets the display label without Shell mnemonic markers.</summary>
	public string Title { get; }

	/// <summary>Gets the optional mnemonic supplied by the Shell label.</summary>
	public string? AccessKey { get; }

	/// <summary>Gets the shortcut text supplied by the command.</summary>
	public string? Shortcut { get; }

	/// <summary>Gets whether the application command manager owns execution.</summary>
	public bool IsApplicationCommand { get; }

	/// <summary>Gets whether the entry can be invoked.</summary>
	public bool IsEnabled { get; }

	/// <summary>Gets whether the entry is checked.</summary>
	public bool IsChecked { get; }

	/// <summary>Gets whether the entry uses radio-check presentation.</summary>
	public bool IsRadio { get; }

	/// <summary>Gets whether the entry separates adjacent menu sections.</summary>
	public bool IsSeparator { get; }

	/// <summary>Gets whether an icon can be loaded for this entry.</summary>
	public bool HasIcon { get; }

	/// <summary>Gets the final child entries for a submenu.</summary>
	public IReadOnlyList<WindowsShellContextMenuEntry> Children { get; }

	internal WindowsShellContextMenuEntry(string id, string title, bool isEnabled, bool isChecked = false, bool isRadio = false, bool isSeparator = false,
		IReadOnlyList<WindowsShellContextMenuEntry>? children = null, WindowsShellContextMenuIcon? icon = null, bool hasNativeIcon = false, bool isApplicationCommand = false, string? shortcut = null)
	{
		Id = id;
		var label = isApplicationCommand ? (title, (string?)null, shortcut) : ParseShellLabel(title);
		Title = label.Item1;
		AccessKey = label.Item2;
		Shortcut = label.Item3;
		IsEnabled = isEnabled;
		IsChecked = isChecked;
		IsRadio = isRadio;
		IsSeparator = isSeparator;
		Children = children is null ? [] : Array.AsReadOnly(children.ToArray());
		Icon = icon;
		HasIcon = hasNativeIcon || icon is not null;
		IsApplicationCommand = isApplicationCommand;
	}

	/// <summary>Creates an application-owned command for inclusion in the final menu.</summary>
	/// <param name="id">The application command identity.</param>
	/// <param name="title">The localized application label.</param>
	/// <param name="isEnabled">Whether the application can execute the command.</param>
	/// <param name="shortcut">The optional shortcut description.</param>
	/// <returns>An entry whose execution remains owned by the application command manager.</returns>
	public static WindowsShellContextMenuEntry CreateApplicationCommand(string id, string title, bool isEnabled, string? shortcut = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);

		ArgumentNullException.ThrowIfNull(title);

		return new(id, title, isEnabled, isApplicationCommand: true, shortcut: shortcut);
	}

	/// <summary>Creates a separator that the menu builder normalizes against adjacent sections.</summary>
	/// <returns>A separator entry.</returns>
	public static WindowsShellContextMenuEntry CreateSeparator()
	{
		return new(string.Empty, string.Empty, false, isSeparator: true);
	}

	internal static (string Title, string? AccessKey, string? Shortcut) ParseShellLabel(string title)
	{
		var shortcutIndex = title.IndexOf('\t');
		var shortcut = shortcutIndex < 0 ? null : title[(shortcutIndex + 1)..];
		var label = shortcutIndex < 0 ? title : title[..shortcutIndex];
		var text = new StringBuilder(label.Length);
		string? accessKey = null;
		for (var index = 0; index < label.Length; index++)
		{
			var character = label[index];
			if (character is not '&' || index + 1 >= label.Length)
			{
				text.Append(character);
				continue;
			}

			character = label[++index];
			if (character is '&')
			{
				text.Append(character);
				continue;
			}

			accessKey ??= character.ToString();
			if (index >= 2 && label[index - 2] is '(' && index + 1 < label.Length && label[index + 1] is ')')
			{
				if (text.Length > 0 && text[^1] is '(')
				{
					text.Length--;
					while (text.Length > 0 && text[^1] is ' ')
					{
						text.Length--;
					}
				}

				index++;
				continue;
			}

			text.Append(character);
		}

		return (text.ToString(), accessKey, shortcut);
	}
}

internal sealed record WindowsShellContextMenuIcon(string Path, int Index = 0, string? PackageFullName = null, string? FallbackPath = null,
	int FallbackIndex = 0, string? FallbackPackageFullName = null);
