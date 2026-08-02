// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

internal static unsafe class ShellItemHelpers
{
	public static WindowsStorableDescriptor CreateDescriptor(IShellItem shellItem, IWindowsItemIdReader itemIdReader)
	{
		ArgumentNullException.ThrowIfNull(shellItem);
		ArgumentNullException.ThrowIfNull(itemIdReader);

		var result = shellItem.GetAttributes(SFGAO_FLAGS.SFGAO_FOLDER | SFGAO_FLAGS.SFGAO_FILESYSTEM | SFGAO_FLAGS.SFGAO_STREAM, out var attributes);
		result.ThrowOnFailure();

		var parsingName = GetRequiredDisplayName(shellItem, SIGDN.SIGDN_DESKTOPABSOLUTEPARSING);
		var name = TryGetDisplayName(shellItem, SIGDN.SIGDN_PARENTRELATIVEFORUI)
			?? TryGetDisplayName(shellItem, SIGDN.SIGDN_NORMALDISPLAY)
			?? parsingName;
		var fileSystemPath = (attributes & SFGAO_FLAGS.SFGAO_FILESYSTEM) != 0
			? TryGetDisplayName(shellItem, SIGDN.SIGDN_FILESYSPATH)
			: null;
		var itemId = itemIdReader.GetItemId(shellItem, parsingName, fileSystemPath);

		var snapshot = new WindowsStorableSnapshot(itemId, name, fileSystemPath, (attributes & SFGAO_FLAGS.SFGAO_FOLDER) != 0, (attributes & SFGAO_FLAGS.SFGAO_STREAM) != 0);
		var address = fileSystemPath is null
			? new StorageAddress(WindowsStorageSource.ShellAddressScheme, parsingName)
			: new StorageAddress(WindowsStorageSource.FileAddressScheme, fileSystemPath);

		return new WindowsStorableDescriptor(itemId, address, new WindowsItemLocator(CopyAbsolutePidl(shellItem), parsingName), snapshot);
	}

	public static string GetRequiredDisplayName(IShellItem shellItem, SIGDN format)
	{
		return TryGetDisplayName(shellItem, format)
			?? throw new InvalidOperationException($"The Shell item does not expose a '{format}' display name.");
	}

	public static string? TryGetDisplayName(IShellItem shellItem, SIGDN format)
	{
		var result = shellItem.GetDisplayName(format, out var displayName);

		if (result.Failed)
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
			PInvoke.CoTaskMemFree(displayName.Value);
		}
	}

	private static unsafe ReadOnlyMemory<byte> CopyAbsolutePidl(IShellItem shellItem)
	{
		ITEMIDLIST* pidl = null;
		var result = PInvoke.SHGetIDListFromObject(shellItem, out pidl);

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
}
