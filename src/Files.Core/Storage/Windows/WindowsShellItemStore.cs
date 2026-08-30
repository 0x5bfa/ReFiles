// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

internal sealed class WindowsShellItemStore
{
	private static readonly Guid _interfaceId = typeof(IDefViewItemStore).GUID;

	private readonly IDefViewItemStore _itemStore;

	private WindowsShellItemStore(IDefViewItemStore itemStore)
	{
		_itemStore = itemStore;
	}

	internal static unsafe WindowsShellItemStore? TryCreate(ReadOnlyMemory<byte> rootPidl)
	{
		if (rootPidl.IsEmpty)
		{
			return null;
		}

		IDefViewItemStore itemStore;
		try
		{
			var createResult = PInvoke.CItemStoreCreateInstance(null, in _interfaceId, out itemStore);
			if (createResult.Failed || itemStore is null)
			{
				return null;
			}
		}
		catch (DllNotFoundException)
		{
			return null;
		}
		catch (EntryPointNotFoundException)
		{
			return null;
		}

		fixed (byte* rootPidlBytes = rootPidl.Span)
		{
			var initializeResult = itemStore.Initialize((ITEMIDLIST*)rootPidlBytes);
			if (initializeResult.Failed)
			{
				return null;
			}
		}

		return new WindowsShellItemStore(itemStore);
	}

	internal unsafe WindowsShellItemStoreReference? TryInsert(IShellFolder parentFolder, ITEMIDLIST* childPidl)
	{
		ArgumentNullException.ThrowIfNull(parentFolder);

		if (childPidl is null)
		{
			return null;
		}

		var createResult = PInvoke.SHCreateItemWithParent<IChildId>(null, parentFolder, in *childPidl, out var childId);
		if (createResult.Failed || childId is null)
		{
			return null;
		}

		var insertResult = _itemStore.InsertItem(childId, ITEM_FLAGS.Valid, null, out var itemKey);

		return insertResult.Succeeded ? new WindowsShellItemStoreReference(this, itemKey) : null;
	}

	internal IShellItem? TryGetItem(in ITEMKEY itemKey, IShellFolder parentFolder)
	{
		ArgumentNullException.ThrowIfNull(parentFolder);

		var interfaceId = typeof(IShellItem).GUID;
		var result = _itemStore.GetItem(in itemKey, parentFolder, null, in interfaceId, out var item);

		return result.Succeeded ? item as IShellItem : null;
	}
}

internal sealed class WindowsShellItemStoreReference
{
	private readonly WindowsShellItemStore _itemStore;

	private readonly ITEMKEY _itemKey;

	internal WindowsShellItemStoreReference(WindowsShellItemStore itemStore, ITEMKEY itemKey)
	{
		_itemStore = itemStore;
		_itemKey = itemKey;
	}

	internal IShellItem? TryGetItem(IShellFolder parentFolder)
	{
		return _itemStore.TryGetItem(in _itemKey, parentFolder);
	}
}
