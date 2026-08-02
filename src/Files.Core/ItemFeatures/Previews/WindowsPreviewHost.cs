// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.Foundation;

namespace Files.Core.ItemFeatures.Previews;

public readonly record struct WindowsPreviewBounds
{
	public int X { get; }

	public int Y { get; }

	public int Width { get; }

	public int Height { get; }

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

public readonly record struct WindowsPreviewColor(byte Red, byte Green, byte Blue);

public sealed record WindowsPreviewHost
{
	public nint WindowHandle { get; }

	public WindowsPreviewBounds Bounds { get; }

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
