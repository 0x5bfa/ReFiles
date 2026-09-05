// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32.Foundation;

namespace Files.Core.Windows;

/// <summary>Provides asynchronous control over a Windows Shell preview session.</summary>
public interface IWindowsShellPreviewSession : IAsyncDisposable
{
	/// <summary>Updates the preview bounds.</summary>
	/// <param name="bounds">The preview bounds.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask SetBoundsAsync(WindowsPreviewBounds bounds, CancellationToken cancellationToken = default);

	/// <summary>Updates the preview colors.</summary>
	/// <param name="background">The background color.</param>
	/// <param name="foreground">The foreground color.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask SetThemeAsync(WindowsPreviewColor background, WindowsPreviewColor foreground, CancellationToken cancellationToken = default);

	/// <summary>Gives focus to the preview handler.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask SetFocusAsync(CancellationToken cancellationToken = default);

	/// <summary>Gets the window that currently has preview focus.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The focused window handle, or zero when no window has focus.</returns>
	ValueTask<HWND> QueryFocusAsync(CancellationToken cancellationToken = default);
}
