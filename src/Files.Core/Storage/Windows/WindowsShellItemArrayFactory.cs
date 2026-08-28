// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers;
using Windows.Win32;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

internal static class WindowsShellItemArrayFactory
{
	internal static IShellItemArray Create(IReadOnlyList<WindowsItemLocator> locators)
	{
		ArgumentNullException.ThrowIfNull(locators);

		return Create(locators.Select(static locator => locator.AbsolutePidl).ToArray());
	}

	internal static unsafe IShellItemArray Create(IReadOnlyList<ReadOnlyMemory<byte>> absolutePidls)
	{
		ArgumentNullException.ThrowIfNull(absolutePidls);

		if (absolutePidls.Count is 0)
		{
			throw new ArgumentException("A Shell selection cannot be empty.", nameof(absolutePidls));
		}

		var handles = new MemoryHandle[absolutePidls.Count];
		var itemIdLists = new nint[absolutePidls.Count];
		var pinnedCount = 0;
		try
		{
			for (var index = 0; index < absolutePidls.Count; index++)
			{
				if (absolutePidls[index].IsEmpty)
				{
					throw new InvalidOperationException("A Windows Shell item does not have an absolute item ID list.");
				}

				handles[index] = absolutePidls[index].Pin();
				itemIdLists[index] = (nint)handles[index].Pointer;
				pinnedCount++;
			}

			fixed (nint* itemIdListPointer = itemIdLists)
			{
				PInvoke.SHCreateShellItemArrayFromIDLists(checked((uint)itemIdLists.Length), (ITEMIDLIST**)itemIdListPointer, out var selection).ThrowOnFailure();

				return selection;
			}
		}
		finally
		{
			for (var index = 0; index < pinnedCount; index++)
			{
				handles[index].Dispose();
			}
		}
	}
}
