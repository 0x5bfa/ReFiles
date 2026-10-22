// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Resources;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.BackgroundServer;

internal sealed class SystemTrayIcon : IDisposable
{
	private const uint CallbackMessage = 0x8001;
	private const uint OpenCommandId = 1;
	private const uint QuitCommandId = 2;
	private static readonly Guid _iconId = new("a1bc2580-99dd-4e58-b4ec-ec9f1a7ca40e");

	private readonly ResourceLoader _resources = ResourceLoader.GetForViewIndependentUse();
	private readonly HICON _icon;
	private readonly SystemTrayIconWindow _iconWindow;
	private readonly uint _taskbarRestartMessageId;
	private readonly string _tooltip;
	private bool _isVisible;
	private bool _notifyIconCreated;

	internal unsafe SystemTrayIcon()
	{
		var iconPath = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "AppTiles", "Dev", "Logo.ico");
		HANDLE iconHandle;
		fixed (char* path = iconPath)
		{
			iconHandle = PInvoke.LoadImage(default, path, GDI_IMAGE_TYPE.IMAGE_ICON, 0, 0, IMAGE_FLAGS.LR_LOADFROMFILE);
		}
		if (iconHandle.IsNull)
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError());
		}

		_icon = new HICON(iconHandle.Value);
		_tooltip = Package.Current.DisplayName;
		_taskbarRestartMessageId = PInvoke.RegisterWindowMessage("TaskbarCreated");
		try
		{
			_iconWindow = new SystemTrayIconWindow(this);
		}
		catch
		{
			PInvoke.DestroyIcon(_icon);
			throw;
		}
	}

	internal void Show()
	{
		if (_isVisible && _notifyIconCreated)
		{
			return;
		}

		_isVisible = true;
		CreateNotifyIcon();
	}

	internal void Hide()
	{
		if (!_isVisible)
		{
			return;
		}

		_isVisible = false;
		DeleteNotifyIcon();
	}

	internal LRESULT WindowProc(HWND window, uint message, WPARAM wParam, LPARAM lParam)
	{
		try
		{
			if (message == CallbackMessage)
			{
				var callbackMessage = unchecked((uint)(lParam.Value & 0xFFFF));
				if (callbackMessage is PInvoke.WM_LBUTTONUP or PInvoke.WM_USER or PInvoke.WM_USER + 1)
				{
					PInvoke.SetForegroundWindow(window);
					OpenApplication();
				}
				else if (callbackMessage is PInvoke.WM_RBUTTONUP or PInvoke.WM_CONTEXTMENU)
				{
					ShowContextMenu();
				}

				return default;
			}

			if (message == PInvoke.WM_DESTROY)
			{
				DeleteNotifyIcon();

				return default;
			}

			if (_taskbarRestartMessageId != 0 && message == _taskbarRestartMessageId)
			{
				_notifyIconCreated = false;
				if (_isVisible)
				{
					CreateNotifyIcon();
				}

				return default;
			}
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine($"Files.BackgroundServer tray callback failed: {exception}");
		}

		return PInvoke.DefWindowProc(window, message, wParam, lParam);
	}

	public void Dispose()
	{
		Hide();
		_iconWindow.Dispose();
		PInvoke.DestroyIcon(_icon);
	}

	private void CreateNotifyIcon()
	{
		var data = CreateNotifyIconData();
		if (!_notifyIconCreated)
		{
			PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
			if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data))
			{
				return;
			}

			_notifyIconCreated = true;
			data.Anonymous.uVersion = 4;
			PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data);
		}
		else
		{
			PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
		}
	}

	private void DeleteNotifyIcon()
	{
		if (!_notifyIconCreated)
		{
			return;
		}

		_notifyIconCreated = false;
		var data = CreateNotifyIconData();
		PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
	}

	private NOTIFYICONDATAW CreateNotifyIconData()
	{
		var data = default(NOTIFYICONDATAW);
		data.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>();
		data.hWnd = _iconWindow.WindowHandle;
		data.uCallbackMessage = CallbackMessage;
		data.hIcon = _icon;
		data.guidItem = _iconId;
		data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP
			| NOTIFY_ICON_DATA_FLAGS.NIF_GUID | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
		data.szTip = _tooltip;

		return data;
	}

	private void ShowContextMenu()
	{
		PInvoke.GetCursorPos(out var point);
		using var menu = PInvoke.CreatePopupMenu_SafeHandle();
		PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_BYCOMMAND, OpenCommandId, GetString("BackgroundServerTrayOpen", "Open ReFiles"));
		PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, string.Empty);
		PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_BYCOMMAND, QuitCommandId, GetString("BackgroundServerTrayQuit", "Quit ReFiles"));
		PInvoke.SetForegroundWindow(_iconWindow.WindowHandle);
		var flags = TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD;
		var commandId = PInvoke.TrackPopupMenuEx(menu, (uint)flags, point.X, point.Y, _iconWindow.WindowHandle, null).Value;
		PInvoke.PostMessage(_iconWindow.WindowHandle, PInvoke.WM_NULL, default, default);
		if (commandId == OpenCommandId)
		{
			OpenApplication();
		}
		else if (commandId == QuitCommandId)
		{
			BackgroundServerHost.RequestQuit();
		}
	}

	private string GetString(string key, string fallback)
	{
		var value = _resources.GetString(key);

		return string.IsNullOrWhiteSpace(value) ? fallback : value;
	}

	private static void OpenApplication()
	{
		if (!BackgroundServerHost.ActivateWindow())
		{
			_ = LaunchApplicationAsync();
		}
	}

	private static async Task LaunchApplicationAsync()
	{
		try
		{
			var appEntries = await Package.Current.GetAppListEntriesAsync().AsTask().ConfigureAwait(false);
			if (appEntries.Count is 0 || !await appEntries[0].LaunchAsync().AsTask().ConfigureAwait(false))
			{
				throw new InvalidOperationException("The ReFiles application could not be launched.");
			}
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine($"Files.BackgroundServer could not launch ReFiles: {exception}");
		}
	}
}
