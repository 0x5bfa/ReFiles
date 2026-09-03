// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.Foundation;
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

		IDefViewItemStore? itemStore;
		HRESULT hr;
		try
		{
			hr = PInvoke.CItemStoreCreateInstance(null, in _interfaceId, out itemStore);
			if (hr.Failed || itemStore is null)
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
			hr = itemStore.Initialize(in *(ITEMIDLIST*)rootPidlBytes);
			if (hr.Failed)
			{
				return null;
			}
		}

		return new WindowsShellItemStore(itemStore);
	}

	internal WindowsShellItemStoreReference? TryInsert(IShellFolder parentFolder, in ITEMIDLIST childPidl)
	{
		ArgumentNullException.ThrowIfNull(parentFolder);

		var hr = PInvoke.SHCreateItemWithParent<IChildId>(null, parentFolder, in childPidl, out var childId);
		if (hr.Failed || childId is null)
		{
			return null;
		}

		hr = _itemStore.InsertItem(childId, ITEM_FLAGS.Valid, null, out var itemKey);

		return hr.Succeeded ? new WindowsShellItemStoreReference(this, itemKey) : null;
	}

	internal IShellItem? TryGetItem(in ITEMKEY itemKey, IShellFolder parentFolder)
	{
		ArgumentNullException.ThrowIfNull(parentFolder);

		var interfaceId = typeof(IShellItem).GUID;
		var hr = _itemStore.GetItem(in itemKey, parentFolder, null, in interfaceId, out var item);

		return hr.Succeeded ? item as IShellItem : null;
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
