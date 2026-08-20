// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace Files.ItemProperties;

internal sealed class ItemPropertiesService : IItemPropertiesService, IDisposable
{
	private readonly nint _owner;
	private readonly HashSet<ItemPropertiesWindow> _windows = [];
	private bool _isDisposed;

	internal ItemPropertiesService(nint owner)
	{
		_owner = owner;
	}

	public unsafe Task ShowAsync(IReadOnlyList<BrowseItemViewModel> items)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		ArgumentNullException.ThrowIfNull(items);

		var window = new ItemPropertiesWindow(items);
		PInvoke.SetWindowLongPtr(new(WindowNative.GetWindowHandle(window)), WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, _owner);
		window.Closed += Window_Closed;
		_windows.Add(window);
		window.Activate();

		return Task.CompletedTask;
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		foreach (var window in _windows.ToArray())
		{
			window.Closed -= Window_Closed;
			window.Close();
		}

		_windows.Clear();
	}

	private void Window_Closed(object sender, WindowEventArgs args)
	{
		if (sender is ItemPropertiesWindow window)
		{
			window.Closed -= Window_Closed;
			_windows.Remove(window);
		}
	}
}
