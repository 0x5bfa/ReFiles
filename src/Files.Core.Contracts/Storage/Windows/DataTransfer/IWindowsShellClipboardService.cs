// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Storage.Windows;

/// <summary>Reads clipboard transfer intent and copies Shell selections.</summary>
public interface IWindowsShellClipboardService
{
	/// <summary>Places a Windows Shell selection on the OLE clipboard.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="move"><see langword="true"/> to advertise a cut operation; otherwise, to advertise copy and link operations.</param>
	/// <param name="ownerWindowHandle">The optional handle of the window that owns the selection.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution or queued Shell work.</param>
	/// <returns>A task that represents the clipboard operation.</returns>
	Task SetItemsAsync(IReadOnlyList<StorableReference> selection, bool move, nint ownerWindowHandle = default, CancellationToken cancellationToken = default);
	/// <summary>Checks for supported storage content on the calling STA.</summary>
	/// <returns>Whether paste may be available.</returns>
	bool HasStorageItems();
	/// <summary>Captures clipboard paths and transfer intent on the calling STA.</summary>
	/// <param name="cancellationToken">Cancels reading before transfer acceptance.</param>
	/// <returns>The captured filesystem content, or null for unsupported content.</returns>
	Task<WindowsShellClipboardSnapshot?> ReadFileSystemItemsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Captures filesystem clipboard content independently of the native clipboard object's lifetime.</summary>
/// <param name="Paths">The copied source paths.</param>
/// <param name="Move">Whether the clipboard requests a move.</param>
public sealed record WindowsShellClipboardSnapshot(IReadOnlyList<string> Paths, bool Move);
