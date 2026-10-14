// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Windows;

internal static unsafe class WindowsShellNativeMenuReader
{
	internal static unsafe IReadOnlyList<WindowsShellNativeMenuEntry> ReadCloudVerbs(DestroyMenuSafeHandle menu, IContextMenu contextMenu)
	{
		var entries = new List<WindowsShellNativeMenuEntry>(2);
		var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Span<char> buffer = stackalloc char[1024];
		for (var index = PInvoke.GetMenuItemCount(menu) - 1; index >= 0 && found.Count < 2; index--)
		{
			var info = default(MENUITEMINFOW);
			info.cbSize = (uint)sizeof(MENUITEMINFOW);
			info.fMask = MENU_ITEM_MASK.MIIM_ID | MENU_ITEM_MASK.MIIM_STATE | MENU_ITEM_MASK.MIIM_FTYPE | MENU_ITEM_MASK.MIIM_STRING
				| MENU_ITEM_MASK.MIIM_BITMAP | MENU_ITEM_MASK.MIIM_CHECKMARKS;
			buffer.Clear();
			fixed (char* title = buffer)
			{
				info.dwTypeData = title;
				info.cch = (uint)buffer.Length;
				if (!PInvoke.GetMenuItemInfo(menu, (uint)index, true, ref info) || info.wID < WindowsShellContextMenuCommandHelper.FirstCommandId || info.wID > WindowsShellContextMenuCommandHelper.LastCommandId)
				{
					continue;
				}

				var ordinal = info.wID - WindowsShellContextMenuCommandHelper.FirstCommandId;
				var verb = WindowsShellContextMenuCommandHelper.GetCanonicalVerb(contextMenu, ordinal);
				if (verb is null || !IsCloudVerb(verb) || !found.Add(verb))
				{
					continue;
				}

				var bitmap = info.hbmpItem;
				if (!IsBitmap(bitmap))
				{
					bitmap = info.fState.HasFlag(MENU_ITEM_STATE.MFS_CHECKED) ? info.hbmpChecked : info.hbmpUnchecked;
				}

				entries.Insert(0, new(verb, new string(buffer[..Math.Min((int)info.cch, buffer.Length)]), ordinal, !info.fState.HasFlag(MENU_ITEM_STATE.MFS_DISABLED),
					info.fState.HasFlag(MENU_ITEM_STATE.MFS_CHECKED), info.fType.HasFlag(MENU_ITEM_TYPE.MFT_RADIOCHECK), IsBitmap(bitmap) ? bitmap : default));
			}
		}

		return entries;
	}

	private static bool IsCloudVerb(string verb)
	{
		return string.Equals(verb, "MakeAvailableOffline", StringComparison.OrdinalIgnoreCase) || string.Equals(verb, "MakeAvailableOnline", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsBitmap(HBITMAP bitmap)
	{
		return (nint)bitmap.Value > 12;
	}
}

internal sealed record WindowsShellNativeMenuEntry(string Verb, string Title, uint Ordinal, bool IsEnabled, bool IsChecked, bool IsRadio, HBITMAP Bitmap);
