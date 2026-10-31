// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

/// <summary>Resolves and compares Shell items synchronously on the caller's STA lane.</summary>
internal static unsafe class WindowsShellItemResolver
{
	internal static IShellItem ResolveOnCurrentSta(string parsingName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		var hr = PInvoke.SHCreateItemFromParsingName(parsingName, null, out IShellItem shellItem);
		hr.ThrowOnFailure();

		return shellItem;
	}

	internal static IShellItem? TryResolveOnCurrentSta(WindowsItemLocator locator)
	{
		ArgumentNullException.ThrowIfNull(locator);

		return TryResolveOnCurrentSta(locator.AbsolutePidl) ?? TryResolveOnCurrentSta(locator.ParsingName);
	}

	internal static IShellItem? TryResolveOnCurrentSta(string parsingName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		var hr = PInvoke.SHCreateItemFromParsingName(parsingName, null, out IShellItem shellItem);

		return hr.Succeeded ? shellItem : null;
	}

	internal static unsafe IShellItem? TryResolveOnCurrentSta(ReadOnlyMemory<byte> absolutePidl)
	{
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

	// Must be called on the ordered Shell STA because it creates and compares COM objects.
	internal static bool AreSamePidlOnCurrentSta(ReadOnlyMemory<byte> firstPidl, ReadOnlyMemory<byte> secondPidl)
	{
		if (firstPidl.IsEmpty || secondPidl.IsEmpty)
		{
			return false;
		}

		if (firstPidl.Span.SequenceEqual(secondPidl.Span))
		{
			return true;
		}

		var first = TryResolveOnCurrentSta(firstPidl);
		var second = TryResolveOnCurrentSta(secondPidl);

		return first is not null && second is not null && AreSame(first, second);
	}

	// Must be called on the ordered Shell STA because it creates and compares COM objects.
	internal static bool IsInFolderOnCurrentSta(ReadOnlyMemory<byte> itemPidl, ReadOnlyMemory<byte> folderPidl, bool recursive)
	{
		if (IsParentPidl(folderPidl, itemPidl, recursive))
		{
			return true;
		}

		var item = TryResolveOnCurrentSta(itemPidl);
		var folder = TryResolveOnCurrentSta(folderPidl);
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
