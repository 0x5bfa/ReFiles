// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Previews;

/// <summary>Controls the lifetime and rendering surface of a Windows preview handler.</summary>
public interface IWindowsPreviewHandlerController : IDisposable
{
	/// <summary>Sets the preview handler site.</summary>
	void SetSite();

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
	void SetWindow(nint windowHandle, WindowsPreviewBounds bounds);

	/// <summary>Updates the preview bounds.</summary>
	/// <param name="bounds">The preview bounds.</param>
	void SetBounds(WindowsPreviewBounds bounds);

	/// <summary>Updates the preview colors.</summary>
	/// <param name="background">The background color.</param>
	/// <param name="foreground">The foreground color.</param>
	void SetTheme(WindowsPreviewColor background, WindowsPreviewColor foreground);

	/// <summary>Starts preview rendering.</summary>
	void DoPreview();

	/// <summary>Gives keyboard focus to the preview handler.</summary>
	void SetFocus();

	/// <summary>Gets the window that currently has preview focus.</summary>
	/// <returns>The focused window handle, or zero when no window has focus.</returns>
	nint QueryFocus();

	/// <summary>Attempts to translate a keyboard message for the preview handler.</summary>
	/// <param name="messagePointer">A pointer to the native message.</param>
	/// <returns><see langword="true"/> when the message was handled.</returns>
	bool TryTranslateAccelerator(nint messagePointer);
}

/// <summary>Creates Windows preview handler controllers.</summary>
public interface IWindowsPreviewHandlerControllerFactory
{
	/// <summary>Creates a controller for a preview handler CLSID.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The created controller.</returns>
	IWindowsPreviewHandlerController Create(Guid handlerClsid);
}
