// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Identifies the screen position from which a Windows Shell command was invoked.
/// </summary>
public readonly struct WindowsShellInvocationPoint
{
	/// <summary>
	/// Gets the horizontal screen coordinate.
	/// </summary>
	public int X { get; }

	/// <summary>
	/// Gets the vertical screen coordinate.
	/// </summary>
	public int Y { get; }

	/// <summary>
	/// Initializes a Shell command invocation point.
	/// </summary>
	/// <param name="x">The horizontal screen coordinate.</param>
	/// <param name="y">The vertical screen coordinate.</param>
	public WindowsShellInvocationPoint(int x, int y)
	{
		X = x;
		Y = y;
	}
}
