// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Extras;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.BackgroundServer;

internal sealed partial class SystemTrayIconWindow : IDisposable
{
	private readonly ManagedWNDPROC _windowProc;
	private readonly string _className = $"Files.BackgroundServer.Tray.{Guid.NewGuid():N}";
	private SystemTrayIcon? _trayIcon;
	private HWND _windowHandle;

	internal HWND WindowHandle => _windowHandle;

	internal unsafe SystemTrayIconWindow(SystemTrayIcon trayIcon)
	{
		_windowProc = WindowProc;
		_trayIcon = trayIcon;
		fixed (char* className = _className)
		{
			var callback = Marshal.GetFunctionPointerForDelegate(_windowProc);
			var windowProcedure = (delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT>)callback;
			var classData = default(WNDCLASSEXW);
			classData.cbSize = (uint)sizeof(WNDCLASSEXW);
			classData.style = WNDCLASS_STYLES.CS_DBLCLKS;
			classData.lpfnWndProc = windowProcedure;
			classData.hInstance = PInvoke.GetModuleHandle(default(PCWSTR));
			classData.lpszClassName = className;
			if (PInvoke.RegisterClassEx(in classData) == 0)
			{
				throw new Win32Exception(Marshal.GetLastPInvokeError());
			}
		}

		_windowHandle = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_LEFT, _className, string.Empty, WINDOW_STYLE.WS_OVERLAPPED, 0, 0, 1, 1, default, null, null, null);
		if (_windowHandle.IsNull)
		{
			var error = Marshal.GetLastPInvokeError();
			UnregisterClass();
			throw new Win32Exception(error);
		}
	}

	public void Dispose()
	{
		if (!_windowHandle.IsNull)
		{
			PInvoke.DestroyWindow(_windowHandle);
			_windowHandle = default;
			UnregisterClass();
		}

		_trayIcon = null;
	}

	private LRESULT WindowProc(HWND window, uint message, WPARAM wParam, LPARAM lParam)
	{
		return _trayIcon?.WindowProc(window, message, wParam, lParam) ?? PInvoke.DefWindowProc(window, message, wParam, lParam);
	}

	private unsafe void UnregisterClass()
	{
		fixed (char* className = _className)
		{
			PInvoke.UnregisterClass(className, PInvoke.GetModuleHandle(default(PCWSTR)));
		}
	}
}
