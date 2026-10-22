// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace FilesLauncher;

internal sealed unsafe class OpenInFolder : IDisposable
{
	private const string WindowClassName = "Files Window Class";
	private const string WindowTitle = "Files Launcher";
	private const nuint TimerIdentifier = 101;
	private const uint TimerDurationMilliseconds = 500;
	private static readonly WindowProcedureDelegate _windowProcedureDelegate = WindowProcedure;
	private static OpenInFolder? _activeInstance;
	private readonly string _openDirectory;
	private IShellWindows? _shellWindows;
	private HWND _window;
	private HINSTANCE _instanceHandle;
	private int _shellWindowCookie;
	private bool _hasShellWindowRegistration;
	private bool _registeredWindowClass;

	private OpenInFolder(string openDirectory)
	{
		_openDirectory = openDirectory;
		PInvoke.CoCreateInstance(typeof(ShellWindowNavigation.ShellWindowsCoclass).GUID, null, CLSCTX.CLSCTX_ALL, out _shellWindows);
	}

	internal static string? Run(string openDirectory)
	{
		var instance = new OpenInFolder(openDirectory);
		_activeInstance = instance;

		try
		{
			instance.RunCore();

			return null;
		}
		finally
		{
			_activeInstance = null;
			instance.Dispose();
		}
	}

	public void Dispose()
	{
		if (!_window.IsNull)
		{
			PInvoke.DestroyWindow(_window);
			_window = default;
		}

		if (_registeredWindowClass)
		{
			fixed (char* className = WindowClassName)
			{
				PInvoke.UnregisterClass(className, _instanceHandle);
			}

			_registeredWindowClass = false;
		}

		if (_hasShellWindowRegistration && _shellWindows is not null)
		{
			_shellWindows.Revoke(_shellWindowCookie);
			_hasShellWindowRegistration = false;
		}

		_shellWindows = null;
	}

	private void RunCore()
	{
		_instanceHandle = PInvoke.GetModuleHandle(default(PCWSTR));
		var windowClass = default(WNDCLASSEXW);
		windowClass.cbSize = (uint)sizeof(WNDCLASSEXW);
		windowClass.lpfnWndProc = (delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT>)Marshal.GetFunctionPointerForDelegate(_windowProcedureDelegate);
		windowClass.hInstance = _instanceHandle;

		fixed (char* className = WindowClassName)
		{
			windowClass.lpszClassName = className;
			if (PInvoke.RegisterClassEx(in windowClass) == 0)
			{
				return;
			}
		}

		_registeredWindowClass = true;
		fixed (char* className = WindowClassName)
		fixed (char* windowTitle = WindowTitle)
		{
			_window = PInvoke.CreateWindowEx(default, className, windowTitle, default, 0, 0, 0, 0, new HWND(new IntPtr(-3)), default, _instanceHandle, null);
		}

		if (_window.IsNull)
		{
			return;
		}

		PInvoke.SetTimer(_window, TimerIdentifier, TimerDurationMilliseconds, null);
		PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNORMAL);

		while (true)
		{
			var result = PInvoke.GetMessage(out var message, HWND.Null, 0, 0);
			if (result.Value <= 0)
			{
				break;
			}

			PInvoke.TranslateMessage(in message);
			PInvoke.DispatchMessage(in message);
		}
	}

	private void OnCreate()
	{
		if (PInvoke.SHCreateItemFromParsingName(_openDirectory, null, out IShellItem? shellItem).Failed || shellItem is null)
		{
			return;
		}

		if (PInvoke.SHGetIDListFromObject(shellItem, out ITEMIDLIST* directoryPidl).Failed || directoryPidl is null)
		{
			return;
		}

		try
		{
			if (_shellWindows is null)
			{
				return;
			}

			var pidlSize = PInvoke.ILGetSize(directoryPidl);
			var pidlBytes = new ReadOnlySpan<byte>((byte*)directoryPidl, checked((int)pidlSize));
			if (PInvoke.InitVariantFromBuffer(pidlBytes, out var pidlVariant).Failed)
			{
				return;
			}

			using (pidlVariant)
			{
				var emptyVariant = default(ComVariant);
				var registerResult = _shellWindows.RegisterPending(unchecked((int)PInvoke.GetCurrentThreadId()), in pidlVariant, in emptyVariant, ShellWindowTypeConstants.SWC_BROWSER, out _shellWindowCookie);
				if (registerResult.Failed)
				{
					return;
				}

				_hasShellWindowRegistration = true;
				_shellWindows.OnNavigate(_shellWindowCookie, in pidlVariant);
			}
		}
		finally
		{
			PInvoke.CoTaskMemFree(directoryPidl);
		}
	}

	private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
	{
		var instance = _activeInstance;
		if (instance is null)
		{
			return PInvoke.DefWindowProc(window, message, wParam, lParam);
		}

		if (message == PInvoke.WM_CREATE)
		{
			instance.OnCreate();
		}
		else if (message == PInvoke.WM_CLOSE)
		{
			PInvoke.DestroyWindow(window);
		}
		else if (message == PInvoke.WM_DESTROY)
		{
			PInvoke.PostQuitMessage(0);
		}
		else if (message == PInvoke.WM_TIMER && wParam.Value == TimerIdentifier)
		{
			PInvoke.PostQuitMessage(0);
			return default;
		}

		return PInvoke.DefWindowProc(window, message, wParam, lParam);
	}

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate LRESULT WindowProcedureDelegate(HWND window, uint message, WPARAM wParam, LPARAM lParam);
}
