// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Storage.Windows;

/// <summary>Owns one menu hierarchy, its commands and lazy icon loads.</summary>
public interface IWindowsShellContextMenuSession : IAsyncDisposable
{
	/// <summary>Gets the completed menu hierarchy.</summary>
	IReadOnlyList<WindowsShellContextMenuEntry> Entries { get; }

	/// <summary>Invokes the original Shell command belonging to an entry in this session.</summary>
	/// <param name="entry">An enabled Shell command returned by this session.</param>
	/// <param name="cancellationToken">Cancels the invocation before it starts.</param>
	/// <returns>Whether the original command was invoked.</returns>
	Task<bool> InvokeAsync(WindowsShellContextMenuEntry entry, CancellationToken cancellationToken = default);

	/// <summary>Loads an entry's icon without changing the menu hierarchy.</summary>
	/// <param name="entry">An entry returned by this session.</param>
	/// <param name="pixelSize">The requested square size in physical pixels.</param>
	/// <param name="cancellationToken">Cancels this caller's wait for the shared icon load.</param>
	/// <returns>Encoded PNG, SVG, or image data, or an empty value when no icon is available.</returns>
	Task<ReadOnlyMemory<byte>> GetIconAsync(WindowsShellContextMenuEntry entry, int pixelSize, CancellationToken cancellationToken = default);

	/// <summary>Loads an entry's icon using the requested Windows theme assets.</summary>
	/// <param name="entry">An entry returned by this session.</param>
	/// <param name="pixelSize">The requested square size in physical pixels.</param>
	/// <param name="theme">The contrast and background theme used by the menu.</param>
	/// <param name="cancellationToken">Cancels this caller's wait for the shared icon load.</param>
	/// <returns>Encoded PNG, SVG, or image data, or an empty value when no icon is available.</returns>
	Task<ReadOnlyMemory<byte>> GetIconAsync(WindowsShellContextMenuEntry entry, int pixelSize, WindowsShellContextMenuIconTheme theme, CancellationToken cancellationToken = default);
}
