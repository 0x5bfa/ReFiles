// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;
using Files.Localization;
using Microsoft.UI.Xaml.Controls;

namespace Files.ItemProperties;

public sealed partial class ShortcutPropertyView : UserControl
{
	internal WindowsShellShortcutProperties Shortcut { get; }

	internal string AdvancedLabel => Strings.Advanced.GetLocalized();

	internal string ChangeIconLabel => Strings.ChangeIcon.GetLocalized();

	internal string CommentLabel => Strings.Comment.GetLocalized();

	internal string Hotkey => FormatHotkey(Shortcut.Hotkey);

	internal string OpenFileLocationLabel => Strings.OpenFileLocation.GetLocalized();

	internal string RunLabel => Strings.Run.GetLocalized();

	internal string ShortcutKeyLabel => Strings.ShortcutKey.GetLocalized();

	internal string ShowCommand => FormatShowCommand(Shortcut.ShowCommand);

	internal string StartInLabel => Strings.StartIn.GetLocalized();

	internal string Target => FormatShortcutTarget(Shortcut);

	internal string TargetLabel => Strings.Target.GetLocalized();

	internal string TargetLocationLabel => Strings.TargetLocation.GetLocalized();

	internal string TargetTypeLabel => Strings.TargetType.GetLocalized();

	internal ShortcutPropertyView(WindowsShellShortcutProperties shortcut)
	{
		Shortcut = shortcut;
		InitializeComponent();
	}

	private static string FormatShortcutTarget(WindowsShellShortcutProperties shortcut)
	{
		var target = shortcut.TargetPath.Contains(' ') ? $"\"{shortcut.TargetPath}\"" : shortcut.TargetPath;

		return string.IsNullOrWhiteSpace(shortcut.Arguments) ? target : $"{target} {shortcut.Arguments}";
	}

	private static string FormatHotkey(ushort hotkey)
	{
		if (hotkey is 0)
		{
			return Strings.None.GetLocalized();
		}

		var parts = new List<string>();
		var modifiers = hotkey >> 8;
		if ((modifiers & 2) is not 0)
		{
			parts.Add(Strings.ControlKey.GetLocalized());
		}

		if ((modifiers & 4) is not 0)
		{
			parts.Add(Strings.AltKey.GetLocalized());
		}

		if ((modifiers & 1) is not 0)
		{
			parts.Add(Strings.ShiftKey.GetLocalized());
		}

		var key = hotkey & 0xFF;
		parts.Add(key is >= 0x30 and <= 0x5A ? ((char)key).ToString() : $"0x{key:X2}");

		return string.Join(" + ", parts);
	}

	private static string FormatShowCommand(int showCommand)
	{
		return showCommand switch
		{
			3 => Strings.Maximized.GetLocalized(),
			7 => Strings.Minimized.GetLocalized(),
			_ => Strings.NormalWindow.GetLocalized(),
		};
	}
}
