// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Prepares Windows Shell data transfer objects without reducing Shell items to physical paths.
/// </summary>
public sealed class WindowsShellDragDropService
{
	private const uint ContextMenuQueryFlags = 0x00000100 | 0x00000800;
	private const string PasteVerb = "paste";

	private readonly WindowsStorageSource _source;

	internal WindowsShellDragDropService(WindowsStorageSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		_source = source;
	}

	/// <summary>Prepares a Windows Shell selection for a WinUI drag surface.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution.</param>
	/// <returns>An apartment-neutral drag source that can be attached on the UI STA.</returns>
	public async Task<WindowsShellDragSource> PrepareDragSourceAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is 0)
		{
			throw new ArgumentException("A drag selection cannot be empty.", nameof(selection));
		}

		var locators = await WindowsShellSelectionResolver.ResolveAsync(_source, selection, cancellationToken).ConfigureAwait(false);

		return new WindowsShellDragSource(locators);
	}

	/// <summary>Prepares a Windows Shell item or folder background as a native drop destination.</summary>
	/// <param name="destination">The Windows Shell item reference.</param>
	/// <param name="background"><see langword="true"/> to request the open folder background target; otherwise, to request the item's own drop target.</param>
	/// <param name="cancellationToken">The token used to cancel destination resolution.</param>
	/// <returns>An apartment-neutral drop target that can create a session on the UI STA.</returns>
	public async Task<WindowsShellDropTarget> PrepareDropTargetAsync(StorableReference destination, bool background, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);

		var locators = await WindowsShellSelectionResolver.ResolveAsync(_source, [destination], cancellationToken).ConfigureAwait(false);

		return new WindowsShellDropTarget(locators[0], background);
	}

	/// <summary>Invokes the destination folder's native Shell paste command.</summary>
	/// <param name="destinationFolder">The destination Windows Shell folder.</param>
	/// <param name="ownerWindowHandle">The window that owns Shell UI displayed by the paste operation.</param>
	/// <param name="cancellationToken">The token used to cancel destination resolution or queued Shell work.</param>
	/// <returns><see langword="true"/> when the destination exposed and invoked its paste command.</returns>
	public async Task<bool> PasteAsync(StorableReference destinationFolder, nint ownerWindowHandle, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destinationFolder);

		if (ownerWindowHandle is 0)
		{
			throw new ArgumentException("A valid owner window handle is required.", nameof(ownerWindowHandle));
		}

		var locators = await WindowsShellSelectionResolver.ResolveAsync(_source, [destinationFolder], cancellationToken).ConfigureAwait(false);

		return await _source.ShellItemResolver.InvokeOperationAsync(locators[0], shellItem => TryInvokePaste(shellItem, (HWND)ownerWindowHandle), cancellationToken).ConfigureAwait(false);
	}

	private static bool TryInvokePaste(IShellItem shellItem, HWND ownerWindow)
	{
		if (shellItem.BindToHandler<IShellFolder>(null, PInvoke.BHID_SFObject, out var folder).Failed || folder is null)
		{
			return false;
		}

		if (folder.CreateViewObject<IContextMenu>(ownerWindow, out var contextMenu).Failed || contextMenu is null)
		{
			return false;
		}

		using var menu = WindowsShellContextMenuCommandHelper.CreateMenu();
		if (contextMenu.QueryContextMenu(menu, 0, WindowsShellContextMenuCommandHelper.FirstCommandId, WindowsShellContextMenuCommandHelper.LastCommandId, ContextMenuQueryFlags).Failed
			|| !TryFindPasteCommand(menu, contextMenu, out var commandOrdinal, out var isEnabled) || !isEnabled)
		{
			return false;
		}

		WindowsShellContextMenuCommandHelper.Invoke(contextMenu, commandOrdinal, new WindowsShellInvocationContext(ownerWindow));

		return true;
	}

	private static unsafe bool TryFindPasteCommand(DestroyMenuSafeHandle menu, IContextMenu contextMenu, out uint commandOrdinal, out bool isEnabled)
	{
		var itemCount = PInvoke.GetMenuItemCount(menu);
		for (var index = 0; index < itemCount; index++)
		{
			var item = default(MENUITEMINFOW);
			item.cbSize = (uint)sizeof(MENUITEMINFOW);
			item.fMask = MENU_ITEM_MASK.MIIM_ID | MENU_ITEM_MASK.MIIM_STATE | MENU_ITEM_MASK.MIIM_SUBMENU;
			if (!PInvoke.GetMenuItemInfo(menu, checked((uint)index), true, ref item))
			{
				continue;
			}

			if (TryMatchPasteCommand(item, contextMenu, out commandOrdinal, out isEnabled))
			{
				return true;
			}
		}

		commandOrdinal = 0;
		isEnabled = false;

		return false;
	}

	private static unsafe bool TryFindPasteCommand(HMENU menu, IContextMenu contextMenu, out uint commandOrdinal, out bool isEnabled)
	{
		var itemCount = PInvoke.GetMenuItemCount(menu);
		for (var index = 0; index < itemCount; index++)
		{
			var item = default(MENUITEMINFOW);
			item.cbSize = (uint)sizeof(MENUITEMINFOW);
			item.fMask = MENU_ITEM_MASK.MIIM_ID | MENU_ITEM_MASK.MIIM_STATE | MENU_ITEM_MASK.MIIM_SUBMENU;
			if (!PInvoke.GetMenuItemInfo(menu, checked((uint)index), true, &item))
			{
				continue;
			}

			if (TryMatchPasteCommand(item, contextMenu, out commandOrdinal, out isEnabled))
			{
				return true;
			}
		}

		commandOrdinal = 0;
		isEnabled = false;

		return false;
	}

	private static bool TryMatchPasteCommand(MENUITEMINFOW item, IContextMenu contextMenu, out uint commandOrdinal, out bool isEnabled)
	{
		if (!item.hSubMenu.IsNull && TryFindPasteCommand(item.hSubMenu, contextMenu, out commandOrdinal, out isEnabled))
		{
			return true;
		}

		if (item.wID >= WindowsShellContextMenuCommandHelper.FirstCommandId && item.wID <= WindowsShellContextMenuCommandHelper.LastCommandId)
		{
			var candidateOrdinal = item.wID - WindowsShellContextMenuCommandHelper.FirstCommandId;
			var canonicalVerb = WindowsShellContextMenuCommandHelper.GetCanonicalVerb(contextMenu, candidateOrdinal);
			if (PasteVerb.Equals(canonicalVerb, StringComparison.OrdinalIgnoreCase))
			{
				commandOrdinal = candidateOrdinal;
				isEnabled = (item.fState & MENU_ITEM_STATE.MFS_DISABLED) is 0;

				return true;
			}
		}

		commandOrdinal = 0;
		isEnabled = false;

		return false;
	}
}
