// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Buffers;
using System.Runtime.InteropServices;
using Files.Core.Storage;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

internal static unsafe class WindowShellHelpers
{
	internal static WindowsStorableDescriptor CreateDescriptor(IShellItem shellItem, IWindowsItemIdReader itemIdReader)
	{
		ArgumentNullException.ThrowIfNull(shellItem);
		ArgumentNullException.ThrowIfNull(itemIdReader);

		return CreateDescriptor(CreateDescriptorData(shellItem), itemIdReader);
	}

	internal static WindowsStorableDescriptorData CreateDescriptorData(
		IShellItem shellItem,
		WindowsItemLocator? parentFolder = null,
		ReadOnlyMemory<byte> absolutePidl = default,
		ReadOnlyMemory<byte> relativePidl = default,
		WindowsShellItemStoreReference? itemStoreReference = null)
	{
		ArgumentNullException.ThrowIfNull(shellItem);

		var attributeMask = SFGAO_FLAGS.SFGAO_FOLDER | SFGAO_FLAGS.SFGAO_FILESYSTEM | SFGAO_FLAGS.SFGAO_STREAM | SFGAO_FLAGS.SFGAO_HIDDEN;
		var result = shellItem.GetAttributes(attributeMask, out var attributes);
		result.ThrowOnFailure();

		var parsingName = GetRequiredDisplayName(shellItem, SIGDN.SIGDN_DESKTOPABSOLUTEPARSING);
		var name = TryGetDisplayName(shellItem, SIGDN.SIGDN_PARENTRELATIVEFORUI)
			?? TryGetDisplayName(shellItem, SIGDN.SIGDN_NORMALDISPLAY)
			?? parsingName;
		var fileSystemPath = (attributes & SFGAO_FLAGS.SFGAO_FILESYSTEM) != 0
			? TryGetDisplayName(shellItem, SIGDN.SIGDN_FILESYSPATH)
			: null;
		var isFolder = (attributes & SFGAO_FLAGS.SFGAO_FOLDER) != 0;
		var metadata = new WindowsStorableMetadata(name, fileSystemPath, isFolder, (attributes & SFGAO_FLAGS.SFGAO_STREAM) != 0, (attributes & SFGAO_FLAGS.SFGAO_HIDDEN) != 0);
		var address = fileSystemPath is null
			? new StorageAddress(WindowsStorageSource.ShellAddressScheme, parsingName)
			: new StorageAddress(WindowsStorageSource.FileAddressScheme, fileSystemPath);

		if (absolutePidl.IsEmpty)
		{
			absolutePidl = CopyAbsolutePidl(shellItem);
		}

		if (parentFolder is not null && relativePidl.IsEmpty)
		{
			relativePidl = GetLastId(absolutePidl);
		}

		return new WindowsStorableDescriptorData(address, new WindowsItemLocator(absolutePidl, parsingName, relativePidl.IsEmpty ? null : parentFolder, relativePidl, itemStoreReference), metadata);
	}

	internal static WindowsStorableDescriptor CreateDescriptor(WindowsStorableDescriptorData data, IWindowsItemIdReader itemIdReader)
	{
		ArgumentNullException.ThrowIfNull(data);
		ArgumentNullException.ThrowIfNull(itemIdReader);

		var itemId = itemIdReader.GetItemId(data.Locator.ParsingName, data.Metadata.FileSystemPath);

		return new WindowsStorableDescriptor(itemId, data.Address, data.Locator, data.Metadata);
	}

	internal static string GetRequiredDisplayName(IShellItem shellItem, SIGDN format)
	{
		return TryGetDisplayName(shellItem, format)
			?? throw new InvalidOperationException($"The Shell item does not expose a '{format}' display name.");
	}

	internal static string? TryGetDisplayName(IShellItem shellItem, SIGDN format)
	{
		var hr = shellItem.GetDisplayName(format, out var displayName);
		if (hr.Failed)
		{
			return null;
		}

		try
		{
			var value = displayName.ToString();

			return string.IsNullOrWhiteSpace(value) ? null : value;
		}
		finally
		{
			PInvoke.CoTaskMemFree(displayName);
		}
	}

	internal static IShellItemArray CreateShellItemArray(IReadOnlyList<WindowsItemLocator> locators)
	{
		ArgumentNullException.ThrowIfNull(locators);

		return CreateShellItemArray(locators.Select(static locator => locator.AbsolutePidl).ToArray());
	}

	internal static IShellItemArray CreateShellItemArray(IReadOnlyList<ReadOnlyMemory<byte>> absolutePidls)
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

	internal static IShellItem ResolveItem(string parsingName)
	{
		if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
		{
			throw new InvalidOperationException("This operation requires an STA thread.");
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		var hr = PInvoke.SHCreateItemFromParsingName(parsingName, null, out IShellItem shellItem);
		hr.ThrowOnFailure();

		return shellItem;
	}

	internal static IShellItem? TryResolveItem(WindowsItemLocator locator)
	{
		if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
		{
			throw new InvalidOperationException("This operation requires an STA thread.");
		}

		ArgumentNullException.ThrowIfNull(locator);

		return TryResolveItem(locator.AbsolutePidl) ?? TryResolveItem(locator.ParsingName);
	}

	internal static IShellItem? TryResolveItem(string parsingName)
	{
		if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
		{
			throw new InvalidOperationException("This operation requires an STA thread.");
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		var hr = PInvoke.SHCreateItemFromParsingName(parsingName, null, out IShellItem shellItem);

		return hr.Succeeded ? shellItem : null;
	}

	internal static unsafe IShellItem? TryResolveItem(ReadOnlyMemory<byte> absolutePidl)
	{
		if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
		{
			throw new InvalidOperationException("This operation requires an STA thread.");
		}

		if (absolutePidl.IsEmpty)
		{
			return null;
		}

		fixed (byte* pidlBytes = absolutePidl.Span)
		{
			ref readonly var pidl = ref *(ITEMIDLIST*)pidlBytes;
			var hr = PInvoke.SHCreateItemFromIDList<IShellItem>(in pidl, out var shellItem);
			if (hr.Failed || shellItem is null)
			{
				return null;
			}

			return shellItem;
		}
	}

	internal static bool AreSamePidl(ReadOnlyMemory<byte> firstPidl, ReadOnlyMemory<byte> secondPidl)
	{
		if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
		{
			throw new InvalidOperationException("This operation requires an STA thread.");
		}

		if (firstPidl.IsEmpty || secondPidl.IsEmpty)
		{
			return false;
		}

		if (firstPidl.Span.SequenceEqual(secondPidl.Span))
		{
			return true;
		}

		var first = TryResolveItem(firstPidl);
		var second = TryResolveItem(secondPidl);

		return first is not null && second is not null && AreSame(first, second);
	}

	internal static bool IsInFolder(ReadOnlyMemory<byte> itemPidl, ReadOnlyMemory<byte> folderPidl, bool recursive)
	{
		if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
		{
			throw new InvalidOperationException("This operation requires an STA thread.");
		}

		if (IsParentPidl(folderPidl, itemPidl, recursive))
		{
			return true;
		}

		var item = TryResolveItem(itemPidl);
		var folder = TryResolveItem(folderPidl);
		if (item is null || folder is null)
		{
			return false;
		}

		if (AreSame(item, folder))
		{
			return true;
		}

		var current = item;
		while (current.GetParent(out var parent).Succeeded && parent is not null)
		{
			if (AreSame(parent, folder))
			{
				return true;
			}

			if (!recursive)
			{
				return false;
			}

			current = parent;
		}

		return false;
	}

	internal static bool MatchesPidl(ReadOnlyMemory<byte> itemPidl, ReadOnlyMemory<byte> folderPidl, bool recursive)
	{
		if (itemPidl.IsEmpty || folderPidl.IsEmpty)
		{
			return false;
		}

		var item = itemPidl.Span;
		var folder = folderPidl.Span;

		if (item.SequenceEqual(folder))
		{
			return true;
		}

		if (folder.Length < sizeof(ushort) || item.Length <= folder.Length || !item[..^sizeof(ushort)].StartsWith(folder[..^sizeof(ushort)]))
		{
			return false;
		}

		if (recursive)
		{
			return true;
		}

		var childOffset = folder.Length - sizeof(ushort);
		var childSize = BitConverter.ToUInt16(item[childOffset..]);

		return childSize >= sizeof(ushort) && childSize + sizeof(ushort) == item.Length - childOffset;
	}

	/// <summary>Copies a PIDL into CoTaskMem memory that the caller must free.</summary>
	internal static ITEMIDLIST* AllocatePidl(ReadOnlySpan<byte> source)
	{
		var nativePidl = (ITEMIDLIST*)Marshal.AllocCoTaskMem(source.Length);
		source.CopyTo(new Span<byte>((byte*)nativePidl, source.Length));

		return nativePidl;
	}

	internal static unsafe ReadOnlyMemory<byte> CopyPidl(ITEMIDLIST* pidl)
	{
		if (pidl is null)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		var size = GetPidlSize(pidl);
		if (size is 0)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		var bytes = GC.AllocateUninitializedArray<byte>(size);
		Marshal.Copy((IntPtr)pidl, bytes, 0, size);

		return bytes;
	}

	private static unsafe ReadOnlyMemory<byte> CopyAbsolutePidl(IShellItem shellItem)
	{
		var result = PInvoke.SHGetIDListFromObject(shellItem, out ITEMIDLIST* pidl);
		if (result.Failed || pidl is null)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		try
		{
			var size = GetPidlSize(pidl);
			if (size is 0)
			{
				return ReadOnlyMemory<byte>.Empty;
			}

			var bytes = GC.AllocateUninitializedArray<byte>(size);
			Marshal.Copy((IntPtr)pidl, bytes, 0, size);

			return bytes;
		}
		finally
		{
			PInvoke.CoTaskMemFree(pidl);
		}
	}

	private static unsafe int GetPidlSize(ITEMIDLIST* pidl)
	{
		var offset = 0;

		while (offset <= int.MaxValue - sizeof(ushort))
		{
			var itemSize = *(ushort*)((byte*)pidl + offset);
			if (itemSize is 0)
			{
				return offset + sizeof(ushort);
			}

			if (itemSize < sizeof(ushort) || offset > int.MaxValue - itemSize)
			{
				return 0;
			}

			offset += itemSize;
		}

		return 0;
	}

	private static ReadOnlyMemory<byte> GetLastId(ReadOnlyMemory<byte> absolutePidl)
	{
		var span = absolutePidl.Span;
		var offset = 0;
		var lastOffset = -1;
		while (offset <= span.Length - sizeof(ushort))
		{
			var itemSize = BitConverter.ToUInt16(span.Slice(offset, sizeof(ushort)));
			if (itemSize is 0)
			{
				return lastOffset < 0 ? ReadOnlyMemory<byte>.Empty : absolutePidl[lastOffset..];
			}

			if (itemSize < sizeof(ushort) || itemSize > span.Length - offset)
			{
				return ReadOnlyMemory<byte>.Empty;
			}

			lastOffset = offset;
			offset += itemSize;
		}

		return ReadOnlyMemory<byte>.Empty;
	}

	private static bool AreSame(IShellItem first, IShellItem second)
	{
		return first.Compare(second, unchecked((uint)_SICHINTF.SICHINT_ALLFIELDS), out var order).Succeeded && order is 0;
	}

	private static unsafe bool IsParentPidl(ReadOnlyMemory<byte> folderPidl, ReadOnlyMemory<byte> itemPidl, bool recursive)
	{
		if (folderPidl.IsEmpty || itemPidl.IsEmpty)
		{
			return false;
		}

		fixed (byte* folderBytes = folderPidl.Span)
		fixed (byte* itemBytes = itemPidl.Span)
		{
			ref readonly var folder = ref *(ITEMIDLIST*)folderBytes;
			ref readonly var item = ref *(ITEMIDLIST*)itemBytes;

			return PInvoke.ILIsParent(in folder, in item, !recursive);
		}
	}
}
