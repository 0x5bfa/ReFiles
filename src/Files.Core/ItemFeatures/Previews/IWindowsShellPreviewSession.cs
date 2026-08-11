// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Previews;

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
	ValueTask<nint> QueryFocusAsync(CancellationToken cancellationToken = default);

	/// <summary>Attempts to translate a keyboard message.</summary>
	/// <param name="messagePointer">A pointer to the native message.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the message was handled.</returns>
	ValueTask<bool> TryTranslateAcceleratorAsync(nint messagePointer, CancellationToken cancellationToken = default);
}
