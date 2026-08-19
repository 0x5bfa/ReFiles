// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>
/// Describes the default command exposed by a Windows Shell item's context menu.
/// </summary>
public sealed class WindowsShellDefaultCommand
{
	/// <summary>
	/// Gets the language-independent command verb, when the context-menu provider exposes one.
	/// </summary>
	public string? CanonicalVerb { get; }

	internal WindowsShellDefaultCommand(string? canonicalVerb)
	{
		CanonicalVerb = canonicalVerb;
	}
}

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

/// <summary>
/// Provides window and input context for invoking a Windows Shell command.
/// </summary>
public sealed class WindowsShellInvocationContext
{
	/// <summary>
	/// Gets the handle of the window that owns any UI displayed by the command.
	/// </summary>
	public nint OwnerWindowHandle { get; }

	/// <summary>
	/// Gets the working directory supplied to the command.
	/// </summary>
	public string? WorkingDirectory { get; }

	/// <summary>
	/// Gets the screen position from which the command was invoked.
	/// </summary>
	public WindowsShellInvocationPoint? InvocationPoint { get; }

	/// <summary>
	/// Initializes context for invoking a Windows Shell command.
	/// </summary>
	/// <param name="ownerWindowHandle">The handle of the window that owns any UI displayed by the command.</param>
	/// <param name="workingDirectory">The working directory supplied to the command.</param>
	/// <param name="invocationPoint">The screen position from which the command was invoked.</param>
	public WindowsShellInvocationContext(nint ownerWindowHandle, string? workingDirectory = null, WindowsShellInvocationPoint? invocationPoint = null)
	{
		if (ownerWindowHandle is 0)
		{
			throw new ArgumentException("A valid owner window handle is required.", nameof(ownerWindowHandle));
		}

		OwnerWindowHandle = ownerWindowHandle;
		WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory;
		InvocationPoint = invocationPoint;
	}
}
