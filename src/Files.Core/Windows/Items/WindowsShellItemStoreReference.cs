// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Windows;

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
