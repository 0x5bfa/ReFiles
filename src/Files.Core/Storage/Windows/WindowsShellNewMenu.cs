// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Retrieves and invokes the Windows Shell New menu for a file-system folder.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsShellNewMenu
{
	private const uint FirstCommandId = 1;
	private const uint LastCommandId = 0x7FFF;
	private const int MenuTextBufferLength = 256;

	private readonly IWindowsShellScheduler _scheduler;

	/// <summary>
	/// Initializes a Shell New menu service.
	/// </summary>
	/// <param name="scheduler">The scheduler that owns the Shell STA used by the menu.</param>
	public WindowsShellNewMenu(IWindowsShellScheduler scheduler)
	{
		ArgumentNullException.ThrowIfNull(scheduler);

		_scheduler = scheduler;
	}

	/// <summary>
	/// Gets the localized New menu items for a file-system folder.
	/// </summary>
	/// <param name="folderPath">The absolute path of the target folder.</param>
	/// <param name="cancellationToken">The token used to cancel the request.</param>
	/// <returns>The items exposed by the Windows Shell.</returns>
	public Task<IReadOnlyList<WindowsShellNewItem>> GetItemsAsync(string folderPath, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

		return _scheduler.InvokeAsync(() => GetItemsOnSta(folderPath), cancellationToken);
	}

	/// <summary>
	/// Invokes a previously retrieved New menu item in a file-system folder.
	/// </summary>
	/// <param name="folderPath">The absolute path of the target folder.</param>
	/// <param name="commandOffset">The command offset returned by <see cref="WindowsShellNewItem.CommandOffset"/>.</param>
	/// <param name="cancellationToken">The token used to cancel the request.</param>
	/// <returns><see langword="true"/> when the Shell accepted the command; otherwise, <see langword="false"/>.</returns>
	public Task<bool> InvokeAsync(string folderPath, uint commandOffset, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

		return _scheduler.InvokeAsync(() => InvokeOnSta(folderPath, commandOffset), cancellationToken);
	}

	private static unsafe IReadOnlyList<WindowsShellNewItem> GetItemsOnSta(string folderPath)
	{
		if (!Directory.Exists(folderPath))
		{
			return [];
		}

		if (!TryCreateMenu(folderPath, out _, out var menu, out var subMenu))
		{
			if (!menu.IsNull)
			{
				PInvoke.DestroyMenu(menu);
			}

			return [];
		}

		try
		{
			var count = PInvoke.GetMenuItemCount(subMenu);
			if (count < 0)
			{
				return [];
			}

			var items = new List<WindowsShellNewItem>(count);
			for (var index = 0; index < count; index++)
			{
				var menuItem = default(MENUITEMINFOW);
				menuItem.cbSize = (uint)sizeof(MENUITEMINFOW);
				menuItem.fMask = MENU_ITEM_MASK.MIIM_FTYPE | MENU_ITEM_MASK.MIIM_ID | MENU_ITEM_MASK.MIIM_STATE | MENU_ITEM_MASK.MIIM_STRING;
				menuItem.dwTypeData = (char*)NativeMemory.Alloc((nuint)MenuTextBufferLength, (nuint)sizeof(char));
				menuItem.cch = MenuTextBufferLength;

				try
				{
					if (!PInvoke.GetMenuItemInfo(subMenu, (uint)index, true, &menuItem)
						|| menuItem.fType.HasFlag(MENU_ITEM_TYPE.MFT_SEPARATOR)
						|| menuItem.dwTypeData.Value is null
						|| menuItem.wID < FirstCommandId)
					{
						continue;
					}

					var name = NormalizeName(menuItem.dwTypeData.ToString());
					if (name.Length is 0)
					{
						continue;
					}

					items.Add(new WindowsShellNewItem(menuItem.wID - FirstCommandId, name, !menuItem.fState.HasFlag(MENU_ITEM_STATE.MFS_DISABLED)));
				}
				finally
				{
					NativeMemory.Free(menuItem.dwTypeData.Value);
				}
			}

			return items;
		}
		finally
		{
			PInvoke.DestroyMenu(menu);
		}
	}

	private static unsafe bool InvokeOnSta(string folderPath, uint commandOffset)
	{
		if (commandOffset > LastCommandId - FirstCommandId || !Directory.Exists(folderPath))
		{
			return false;
		}

		if (!TryCreateMenu(folderPath, out var contextMenu, out var menu, out _))
		{
			if (!menu.IsNull)
			{
				PInvoke.DestroyMenu(menu);
			}

			return false;
		}

		try
		{
			var commandInfo = new CMINVOKECOMMANDINFO
			{
				cbSize = (uint)sizeof(CMINVOKECOMMANDINFO),
				lpVerb = (PCSTR)(byte*)(nuint)commandOffset,
				nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL,
			};

			return contextMenu.InvokeCommand(commandInfo).Succeeded;
		}
		finally
		{
			PInvoke.DestroyMenu(menu);
		}
	}

	private static unsafe bool TryCreateMenu(string folderPath, out IContextMenu contextMenu, out HMENU menu, out HMENU subMenu)
	{
		contextMenu = null!;
		menu = default;
		subMenu = default;

		var shellItemResult = PInvoke.SHCreateItemFromParsingName(folderPath, null, out IShellItem shellItem);
		if (shellItemResult.Failed)
		{
			return false;
		}

		var createResult = PInvoke.CoCreateInstance(CLSID.CLSID_NewMenu, null, CLSCTX.CLSCTX_INPROC_SERVER, out IContextMenu? createdMenu);
		if (createResult.Failed || createdMenu is null || createdMenu is not IContextMenu2 contextMenu2 || createdMenu is not IShellExtInit shellExtInit)
		{
			return false;
		}

		ITEMIDLIST* folderPidl = null;
		try
		{
			var pidlResult = PInvoke.SHGetIDListFromObject(shellItem, out folderPidl);
			if (pidlResult.Failed || folderPidl is null)
			{
				return false;
			}

			var initializeResult = shellExtInit.Initialize(folderPidl, null, default);
			if (initializeResult.Failed)
			{
				return false;
			}
		}
		finally
		{
			if (folderPidl is not null)
			{
				PInvoke.CoTaskMemFree(folderPidl);
			}
		}

		menu = PInvoke.CreatePopupMenu();
		if (menu.IsNull)
		{
			return false;
		}

		var queryResult = createdMenu.QueryContextMenu(menu, 0, FirstCommandId, LastCommandId, 0);
		if (queryResult.Failed)
		{
			PInvoke.DestroyMenu(menu);
			menu = default;

			return false;
		}

		subMenu = PInvoke.GetSubMenu(menu, 0);
		if (subMenu.IsNull)
		{
			PInvoke.DestroyMenu(menu);
			menu = default;

			return false;
		}

		var initializePopupResult = contextMenu2.HandleMenuMsg(PInvoke.WM_INITMENUPOPUP, (nuint)subMenu.Value, 0);
		if (initializePopupResult.Failed)
		{
			PInvoke.DestroyMenu(menu);
			menu = default;

			return false;
		}

		contextMenu = createdMenu;

		return true;
	}

	private static string NormalizeName(string name)
	{
		return name.Replace("&", string.Empty, StringComparison.Ordinal).Trim();
	}
}

/// <summary>
/// Describes one item exposed by the Windows Shell New menu.
/// </summary>
public sealed class WindowsShellNewItem
{
	/// <summary>
	/// Initializes a Shell New menu item.
	/// </summary>
	/// <param name="commandOffset">The command offset used by <c>IContextMenu::InvokeCommand</c>.</param>
	/// <param name="name">The localized display name.</param>
	/// <param name="isEnabled">A value indicating whether the Shell enabled the item.</param>
	internal WindowsShellNewItem(uint commandOffset, string name, bool isEnabled)
	{
		CommandOffset = commandOffset;
		Name = name;
		IsEnabled = isEnabled;
	}

	/// <summary>
	/// Gets the command offset used to invoke the item.
	/// </summary>
	public uint CommandOffset { get; }

	/// <summary>
	/// Gets the localized display name.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets a value indicating whether the Shell enabled the item.
	/// </summary>
	public bool IsEnabled { get; }
}
