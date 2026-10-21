// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.BackgroundServer;

internal static class BackgroundServerHost
{
	private static readonly Lock _syncRoot = new();
	private static readonly Dictionary<Guid, uint> _clients = [];
	private static readonly List<ClientWindow> _windows = [];
	private static DispatcherQueueController? _dispatcherController;
	private static SystemTrayIcon? _trayIcon;
	private static IDisposable? _backgroundLease;
	private static bool _isQuitting;
	private static bool _isShuttingDown;

	internal static void Initialize()
	{
		try
		{
			_dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files.BackgroundServer could not initialize its dispatcher: {exception}");
		}

		try
		{
			_ = Program.Storage.Value;
			_ = Program.Thumbnails.Value;
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files.BackgroundServer could not initialize shared services: {exception}");
		}
	}

	internal static void AddClient(Guid clientId, int processId)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);

		lock (_syncRoot)
		{
			ObjectDisposedException.ThrowIf(_isQuitting || _isShuttingDown, typeof(BackgroundServerHost));
			_clients.Add(clientId, (uint)processId);
		}
	}

	internal static void RegisterWindow(Guid clientId, long windowHandle)
	{
		var handle = new HWND((nint)windowHandle);
		if (handle.IsNull || !PInvoke.IsWindow(handle) || PInvoke.GetWindowThreadProcessId(handle, out var processId) is 0)
		{
			return;
		}

		bool closeWindow;
		lock (_syncRoot)
		{
			if (!_clients.TryGetValue(clientId, out var clientProcessId) || clientProcessId != processId)
			{
				return;
			}

			_windows.RemoveAll(window => window.Handle == handle);
			_windows.Add(new ClientWindow(clientId, handle, processId));
			_backgroundLease ??= Program.Lifetime.Acquire();
			closeWindow = _isQuitting;
		}

		if (closeWindow)
		{
			PInvoke.PostMessage(handle, PInvoke.WM_CLOSE, default, default);

			return;
		}

		UpdateTrayVisibility();
	}

	internal static void SetActiveWindow(Guid clientId, long windowHandle)
	{
		var handle = new HWND((nint)windowHandle);
		lock (_syncRoot)
		{
			var index = _windows.FindLastIndex(window => window.ClientId == clientId && window.Handle == handle);
			if (index < 0)
			{
				return;
			}

			var window = _windows[index];
			_windows.RemoveAt(index);
			_windows.Add(window);
		}
	}

	internal static void UnregisterWindow(Guid clientId, long windowHandle)
	{
		var handle = new HWND((nint)windowHandle);
		lock (_syncRoot)
		{
			_windows.RemoveAll(window => window.ClientId == clientId && window.Handle == handle);
		}

		UpdateTrayVisibility();
	}

	internal static void RemoveClient(Guid clientId)
	{
		lock (_syncRoot)
		{
			_clients.Remove(clientId);
			_windows.RemoveAll(window => window.ClientId == clientId);
		}

		UpdateTrayVisibility();
	}

	internal static bool ActivateWindow()
	{
		ClientWindow[] windows;
		lock (_syncRoot)
		{
			windows = [.. _windows.AsEnumerable().Reverse()];
		}

		foreach (var window in windows)
		{
			if (!IsClientWindow(window))
			{
				continue;
			}

			if (PInvoke.IsIconic(window.Handle))
			{
				PInvoke.ShowWindow(window.Handle, SHOW_WINDOW_CMD.SW_RESTORE);
			}

			if (PInvoke.SetForegroundWindow(window.Handle))
			{
				return true;
			}
		}

		return false;
	}

	internal static void RequestQuit()
	{
		ClientWindow[] windows;
		IDisposable? backgroundLease;
		lock (_syncRoot)
		{
			_isQuitting = true;
			windows = [.. _windows];
			backgroundLease = _backgroundLease;
			_backgroundLease = null;
		}

		backgroundLease?.Dispose();
		UpdateTrayVisibility();
		foreach (var window in windows)
		{
			if (IsClientWindow(window))
			{
				PInvoke.PostMessage(window.Handle, PInvoke.WM_CLOSE, default, default);
			}
		}
	}

	internal static async Task ShutdownAsync()
	{
		DispatcherQueueController? controller;
		lock (_syncRoot)
		{
			_isShuttingDown = true;
			_backgroundLease?.Dispose();
			_backgroundLease = null;
			controller = _dispatcherController;
			_dispatcherController = null;
		}

		if (controller is null)
		{
			return;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		if (controller.DispatcherQueue.TryEnqueue(() =>
		{
			try
			{
				_trayIcon?.Dispose();
				_trayIcon = null;
				completion.SetResult();
			}
			catch (Exception exception)
			{
				completion.SetException(exception);
			}
		}))
		{
			await completion.Task.ConfigureAwait(false);
		}

		await controller.ShutdownQueueAsync().AsTask().ConfigureAwait(false);
	}

	private static void UpdateTrayVisibility()
	{
		var controller = _dispatcherController;
		if (controller is null)
		{
			return;
		}

		bool hasWindows;
		lock (_syncRoot)
		{
			hasWindows = !_isQuitting && !_isShuttingDown && (_backgroundLease is not null || _windows.Count > 0);
		}

		if (!controller.DispatcherQueue.TryEnqueue(() => RefreshTrayIcon(hasWindows)))
		{
			Debug.WriteLine("Files.BackgroundServer could not queue a tray update.");
		}
	}

	private static void RefreshTrayIcon(bool isVisible)
	{
		try
		{
			if (isVisible)
			{
				(_trayIcon ??= new SystemTrayIcon()).Show();
			}
			else
			{
				_trayIcon?.Hide();
			}
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files.BackgroundServer could not update the tray icon: {exception}");
		}
	}

	private static bool IsClientWindow(ClientWindow window)
	{
		return !window.Handle.IsNull && PInvoke.IsWindow(window.Handle)
			&& PInvoke.GetWindowThreadProcessId(window.Handle, out var processId) is not 0 && processId == window.ProcessId;
	}

	private readonly record struct ClientWindow(Guid ClientId, HWND Handle, uint ProcessId);
}
