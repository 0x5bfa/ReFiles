// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32.Foundation;

namespace Files.Core.Windows;

/// <summary>Controls the lifetime and rendering surface of a Windows preview handler.</summary>
public interface IWindowsPreviewHandlerController : IDisposable
{
	/// <summary>Sets the preview handler site.</summary>
	void SetSite();

	/// <summary>Sets the preview handler site and accelerator forwarding callback.</summary>
	/// <param name="hostWindow">The preview host window.</param>
	/// <param name="acceleratorForwarder">The callback that asynchronously forwards accelerator messages.</param>
	void SetSite(HWND hostWindow, WindowsPreviewAcceleratorForwarder? acceleratorForwarder)
	{
		SetSite();
	}

	/// <summary>Initializes the handler with a file stream path.</summary>
	/// <param name="fileSystemPath">The path of the file to preview.</param>
	/// <returns><see langword="true"/> when initialization succeeds.</returns>
	bool TryInitializeWithStream(string fileSystemPath);

	/// <summary>Initializes the handler with a Shell parsing name.</summary>
	/// <param name="parsingName">The Shell parsing name.</param>
	/// <returns><see langword="true"/> when initialization succeeds.</returns>
	bool TryInitializeWithItem(string parsingName);

	/// <summary>Initializes the handler with a file path.</summary>
	/// <param name="fileSystemPath">The path of the file to preview.</param>
	/// <returns><see langword="true"/> when initialization succeeds.</returns>
	bool TryInitializeWithFile(string fileSystemPath);

	/// <summary>Associates the handler with its host window.</summary>
	/// <param name="windowHandle">The host window handle.</param>
	/// <param name="bounds">The preview bounds.</param>
	void SetWindow(HWND windowHandle, WindowsPreviewBounds bounds);

	/// <summary>Updates the preview bounds.</summary>
	/// <param name="bounds">The preview bounds.</param>
	void SetBounds(WindowsPreviewBounds bounds);

	/// <summary>Updates the preview colors.</summary>
	/// <param name="background">The background color.</param>
	/// <param name="foreground">The foreground color.</param>
	void SetTheme(WindowsPreviewColor background, WindowsPreviewColor foreground);

	/// <summary>Applies the Windows system preview colors and font when supported.</summary>
	void ApplySystemVisuals()
	{
	}

	/// <summary>Starts preview rendering.</summary>
	void DoPreview();

	/// <summary>Gives keyboard focus to the preview handler.</summary>
	void SetFocus();

	/// <summary>Gets the window that currently has preview focus.</summary>
	/// <returns>The focused window handle, or zero when no window has focus.</returns>
	HWND QueryFocus();
}
