// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.


namespace Files.Core.Storage.Windows;

/// <summary>Identifies the native window that hosts a preview handler.</summary>
/// <remarks>The provider validates window liveness immediately before creating a session.</remarks>
public sealed record WindowsPreviewHost
{
	/// <summary>Gets the borrowed native host window handle; the caller retains ownership.</summary>
	public nint WindowHandle { get; }
	/// <summary>Gets the host bounds.</summary>
	public WindowsPreviewBounds Bounds { get; }
	/// <summary>Gets the callback that asynchronously forwards accelerator messages.</summary>
	public WindowsPreviewAcceleratorForwarder? AcceleratorForwarder { get; }

	/// <summary>Initializes a preview host.</summary>
	/// <param name="windowHandle">The borrowed native host window handle; the caller retains ownership.</param>
	/// <param name="bounds">The host bounds.</param>
	public WindowsPreviewHost(nint windowHandle, WindowsPreviewBounds bounds) : this(windowHandle, bounds, null)
	{
	}

	/// <summary>Initializes a preview host.</summary>
	/// <param name="windowHandle">The borrowed native host window handle; the caller retains ownership.</param>
	/// <param name="bounds">The host bounds.</param>
	/// <param name="acceleratorForwarder">The callback that asynchronously forwards accelerator messages.</param>
	public WindowsPreviewHost(nint windowHandle, WindowsPreviewBounds bounds, WindowsPreviewAcceleratorForwarder? acceleratorForwarder)
	{
		if (windowHandle == 0)
		{
			throw new ArgumentException("A preview host window handle is required.", nameof(windowHandle));
		}

		WindowHandle = windowHandle;
		Bounds = bounds;
		AcceleratorForwarder = acceleratorForwarder;
	}
}
