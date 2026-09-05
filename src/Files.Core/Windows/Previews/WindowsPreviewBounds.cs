// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

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
