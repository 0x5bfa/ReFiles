// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Adapters;
using Files.Commands;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Windows.Foundation;

namespace Files.Presentation;

/// <summary>Adapts native clipboard, menus, and drag/drop for one browse pane.</summary>
internal interface IBrowseInteraction : IAsyncDisposable
{
	bool CanCopy { get; }

	bool CanPaste { get; }
	bool CanShowNew { get; }
	bool CanShowShellContextMenu { get; }
	IWindowsShellDataTransfer CaptureDataTransfer(global::Windows.ApplicationModel.DataTransfer.DataPackageView view)
	{
		throw new NotSupportedException("This surface does not support Windows data transfer.");
	}

	void Initialize();
	void Update(BrowseUpdateFlags flags, bool selectionChanged);
	void UpdateSelection(IReadOnlyList<StorableReference> selection);
	Task<bool> IsShellDesktopAsync(StorableReference reference, CancellationToken cancellationToken);
	bool SupportsShellDragDrop(StorableReference reference);
	Task<IWindowsShellDragSource?> PrepareShellDragSourceAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken);
	Task<IWindowsShellDropTarget?> PrepareShellDropTargetAsync(StorableReference destination, bool background, CancellationToken cancellationToken);
	Task CopySelectionAsync(bool move, CancellationToken cancellationToken = default);
	Task CopyItemsAsync(IReadOnlyList<StorableReference> selection, bool move, CancellationToken cancellationToken);
	Task PasteFromClipboardAsync(CancellationToken cancellationToken = default);
	Task<IReadOnlyList<WindowsShellNewItem>> GetNewItemsAsync(CancellationToken cancellationToken = default);
	Task InvokeNewItemAsync(WindowsShellNewItem item, CancellationToken cancellationToken = default);
	Task<IWindowsShellContextMenuSession?> CreateShellContextMenuAsync(IReadOnlyList<StorableReference> selection, IReadOnlyList<WindowsShellContextMenuEntry> applicationEntries,
		WindowsShellContextMenuEntry? expandedMenuEntry, Point clientPoint, double rasterizationScale, bool extendedVerbs, CancellationToken cancellationToken);
	CommandState GetContextualCommandState(string commandId, bool hideWhenShellDisabled);
	Task<bool> InvokeContextualCommandAsync(string commandId, CancellationToken cancellationToken = default);
	Task<bool> ShowShellPropertiesAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default);
	Task<IWindowsShellContextMenuTarget?> GetShellContextMenuTargetAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default);
	bool ShowShellContextMenu(IWindowsShellContextMenuTarget target, Point clientPoint, double rasterizationScale);
}
