// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.Foundation;

namespace Files.Core.ItemFeatures.Previews;

/// <summary>Describes the bounds of a Windows preview host.</summary>
public readonly record struct WindowsPreviewBounds
{
	/// <summary>Gets the horizontal position.</summary>
	public int X { get; }

	/// <summary>Gets the vertical position.</summary>
	public int Y { get; }

	/// <summary>Gets the host width.</summary>
	public int Width { get; }

	/// <summary>Gets the host height.</summary>
	public int Height { get; }

	/// <summary>Initializes preview bounds.</summary>
	/// <param name="x">The horizontal position.</param>
	/// <param name="y">The vertical position.</param>
	/// <param name="width">The host width.</param>
	/// <param name="height">The host height.</param>
	public WindowsPreviewBounds(int x, int y, int width, int height)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(width);
		ArgumentOutOfRangeException.ThrowIfNegative(height);

		X = x;
		Y = y;
		Width = width;
		Height = height;
	}
}

/// <summary>Describes an RGB color used by a Windows preview handler.</summary>
/// <param name="Red">The red component.</param>
/// <param name="Green">The green component.</param>
/// <param name="Blue">The blue component.</param>
public readonly record struct WindowsPreviewColor(byte Red, byte Green, byte Blue);

/// <summary>Identifies the native window that hosts a preview handler.</summary>
public sealed record WindowsPreviewHost
{
	/// <summary>Gets the native host window handle.</summary>
	public nint WindowHandle { get; }

	/// <summary>Gets the host bounds.</summary>
	public WindowsPreviewBounds Bounds { get; }

	/// <summary>Initializes a preview host.</summary>
	/// <param name="windowHandle">The native host window handle.</param>
	/// <param name="bounds">The host bounds.</param>
	public WindowsPreviewHost(nint windowHandle, WindowsPreviewBounds bounds)
	{
		if (windowHandle == 0)
		{
			throw new ArgumentException("A preview host window handle is required.", nameof(windowHandle));
		}

		if (!PInvoke.IsWindow((HWND)windowHandle))
		{
			throw new ArgumentException("The preview host window handle is not valid.", nameof(windowHandle));
		}

		WindowHandle = windowHandle;
		Bounds = bounds;
	}
}
