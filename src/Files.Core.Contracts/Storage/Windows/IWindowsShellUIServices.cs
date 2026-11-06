// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Windows.ApplicationModel.DataTransfer;
using System.Drawing;

namespace Files.Core.Storage.Windows;

/// <summary>Provides the borrowed Windows integrations configured for one UI runtime.</summary>
public interface IWindowsShellUIServices
{
	/// <summary>Gets the Windows source identity without exposing its implementation.</summary>
	StorageSourceId SourceId { get; }
	/// <summary>Gets clipboard operations.</summary>
	IWindowsShellClipboardService Clipboard { get; }
	/// <summary>Gets drag and drop operations.</summary>
	IWindowsShellDragDropService DragDrop { get; }
	/// <summary>Gets property-sheet operations.</summary>
	IWindowsShellPropertiesService Properties { get; }
	/// <summary>Gets stateless property editing actions.</summary>
	IWindowsShellPropertyActions PropertyActions { get; }
	/// <summary>Gets filesystem inspection using the runtime's attribute executor.</summary>
	IItemPropertiesFileSystem ItemPropertiesFileSystem { get; }
	/// <summary>Gets contextual command operations.</summary>
	IWindowsShellContextualCommandService ContextualCommands { get; }
	/// <summary>Gets default item activation operations.</summary>
	IWindowsShellDefaultCommandInvoker DefaultCommands { get; }
	/// <summary>Gets preview session creation.</summary>
	IWindowsShellPreviewSessionFactory? Previews { get; }
	/// <summary>Gets copied filesystem paths for the registered cloud synchronization roots.</summary>
	/// <returns>The registered root paths.</returns>
	IReadOnlyList<string> GetSyncRootPaths();

	/// <summary>Tests whether an item identifies the Shell desktop.</summary>
	/// <param name="reference">The captured item.</param>
	/// <param name="cancellationToken">Cancels resolution.</param>
	/// <returns>Whether the item is the desktop.</returns>
	ValueTask<bool> IsShellDesktopAsync(StorableReference reference, CancellationToken cancellationToken = default);
	/// <summary>Creates a managed context-menu session.</summary>
	/// <param name="selection">The captured selection.</param>
	/// <param name="context">The invocation's window and coordinates.</param>
	/// <param name="applicationEntries">The application's commands.</param>
	/// <param name="expandedMenuEntry">The optional classic-menu command.</param>
	/// <param name="extendedVerbs">Whether to include extended verbs.</param>
	/// <param name="cancellationToken">Cancels preparation.</param>
	/// <returns>The owned session, or null for unsupported selections.</returns>
	Task<IWindowsShellContextMenuSession?> CreateContextMenuAsync(IReadOnlyList<StorableReference> selection, WindowsShellInvocationContext context,
		IReadOnlyList<WindowsShellContextMenuEntry> applicationEntries, WindowsShellContextMenuEntry? expandedMenuEntry, bool extendedVerbs, CancellationToken cancellationToken = default);
	/// <summary>Captures an opaque classic-menu target.</summary>
	/// <param name="selection">The captured selection.</param>
	/// <param name="cancellationToken">Cancels preparation.</param>
	/// <returns>The target, or null when unsupported.</returns>
	Task<IWindowsShellContextMenuTarget?> GetContextMenuTargetAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default);
	/// <summary>Shows a classic menu on the calling UI STA.</summary>
	/// <param name="owner">The borrowed owner window.</param>
	/// <param name="target">The target returned by this service.</param>
	/// <param name="point">The screen position.</param>
	/// <returns>Whether a command was invoked.</returns>
	bool ShowContextMenu(nint owner, IWindowsShellContextMenuTarget target, Point point);

}

/// <summary>Identifies an opaque classic-menu target captured by the Windows service.</summary>
public interface IWindowsShellContextMenuTarget { }

/// <summary>Retains a native data transfer object on its originating STA without exposing COM.</summary>
public interface IWindowsShellDataTransfer { }

/// <summary>Prepares Windows drag sources and drop targets.</summary>
public interface IWindowsShellDragDropService
{
	/// <summary>Captures a native data object on the calling STA before asynchronous work.</summary>
	/// <param name="view">The incoming WinRT data view.</param>
	/// <returns>The retained data object, for use on the same STA.</returns>
	IWindowsShellDataTransfer CaptureDataTransfer(DataPackageView view);
	/// <summary>Prepares a drag source.</summary>
	/// <param name="selection">The selected item references.</param>
	/// <param name="cancellationToken">Cancels resolution.</param>
	/// <returns>The drag source to attach on the UI STA.</returns>
	Task<IWindowsShellDragSource> PrepareDragSourceAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default);
	/// <summary>Prepares a drop target.</summary>
	/// <param name="destination">The destination item.</param>
	/// <param name="background">Whether to target its background.</param>
	/// <param name="cancellationToken">Cancels resolution.</param>
	/// <returns>The prepared target.</returns>
	Task<IWindowsShellDropTarget> PrepareDropTargetAsync(StorableReference destination, bool background, CancellationToken cancellationToken = default);
	/// <summary>Invokes native Shell paste for virtual destinations.</summary>
	/// <param name="destinationFolder">The destination folder.</param>
	/// <param name="ownerWindowHandle">The borrowed owner window.</param>
	/// <param name="cancellationToken">Cancels preparation.</param>
	/// <returns>Whether paste was invoked.</returns>
	Task<bool> PasteAsync(StorableReference destinationFolder, nint ownerWindowHandle, CancellationToken cancellationToken = default);
}

/// <summary>Creates drop sessions on the incoming data object's STA.</summary>
public interface IWindowsShellDropTarget
{
	/// <summary>Creates a session on the calling STA.</summary>
	/// <param name="dataTransfer">The captured native data object.</param>
	/// <param name="ownerWindowHandle">The borrowed owner window.</param>
	/// <param name="session">Receives the session to use and dispose on this STA.</param>
	/// <returns>Whether the Shell exposes a drop target.</returns>
	bool TryCreateSession(IWindowsShellDataTransfer dataTransfer, nint ownerWindowHandle, out IWindowsShellDropSession? session);
}
