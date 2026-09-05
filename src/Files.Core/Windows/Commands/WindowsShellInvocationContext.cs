// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

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
