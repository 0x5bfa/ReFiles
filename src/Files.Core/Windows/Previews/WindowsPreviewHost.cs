// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32;
using Windows.Win32.Foundation;

namespace Files.Core.Windows;

/// <summary>Identifies the native window that hosts a preview handler.</summary>
public sealed record WindowsPreviewHost
{
	/// <summary>Gets the native host window handle.</summary>
	public HWND WindowHandle { get; }

	/// <summary>Gets the host bounds.</summary>
	public WindowsPreviewBounds Bounds { get; }

	/// <summary>Gets the callback that asynchronously forwards accelerator messages.</summary>
	public WindowsPreviewAcceleratorForwarder? AcceleratorForwarder { get; }

	/// <summary>Initializes a preview host.</summary>
	/// <param name="windowHandle">The native host window handle.</param>
	/// <param name="bounds">The host bounds.</param>
	public WindowsPreviewHost(HWND windowHandle, WindowsPreviewBounds bounds)
		: this(windowHandle, bounds, null)
	{
	}

	/// <summary>Initializes a preview host.</summary>
	/// <param name="windowHandle">The native host window handle.</param>
	/// <param name="bounds">The host bounds.</param>
	/// <param name="acceleratorForwarder">The callback that asynchronously forwards accelerator messages.</param>
	public WindowsPreviewHost(HWND windowHandle, WindowsPreviewBounds bounds, WindowsPreviewAcceleratorForwarder? acceleratorForwarder)
	{
		if (windowHandle.IsNull)
		{
			throw new ArgumentException("A preview host window handle is required.", nameof(windowHandle));
		}

		if (!PInvoke.IsWindow(windowHandle))
		{
			throw new ArgumentException("The preview host window handle is not valid.", nameof(windowHandle));
		}

		WindowHandle = windowHandle;
		Bounds = bounds;
		AcceleratorForwarder = acceleratorForwarder;
	}
}
