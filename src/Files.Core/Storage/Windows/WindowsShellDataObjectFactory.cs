// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

internal static class WindowsShellDataObjectFactory
{
	internal static IDataObject Create(IReadOnlyList<WindowsItemLocator> locators, HWND ownerWindow)
	{
		ArgumentNullException.ThrowIfNull(locators);

		if (locators.Count is 0)
		{
			throw new ArgumentException("A Shell selection cannot be empty.", nameof(locators));
		}

		if (TryCreateFromCommonParent(locators, ownerWindow) is { } dataObject)
		{
			return dataObject;
		}

		var shellItemArray = WindowsShellItemArrayFactory.Create(locators);
		shellItemArray.BindToHandler<IDataObject>(null, PInvoke.BHID_DataObject, out dataObject).ThrowOnFailure();

		return dataObject ?? throw new InvalidOperationException("The Windows Shell selection did not provide a data object.");
	}

	private static unsafe IDataObject? TryCreateFromCommonParent(IReadOnlyList<WindowsItemLocator> locators, HWND ownerWindow)
	{
		var parentLocator = locators[0].ParentFolder;
		if (parentLocator is null || locators[0].RelativePidl.IsEmpty)
		{
			return null;
		}

		for (var index = 1; index < locators.Count; index++)
		{
			if (locators[index].ParentFolder is not { } candidateParent || locators[index].RelativePidl.IsEmpty || !parentLocator.AbsolutePidl.Span.SequenceEqual(candidateParent.AbsolutePidl.Span))
			{
				return null;
			}
		}

		var parentItem = WindowsShellItemResolver.TryCreateFromPidl(parentLocator.AbsolutePidl);
		if (parentItem is null || parentItem.BindToHandler<IShellFolder>(null, PInvoke.BHID_SFObject, out var parentFolder).Failed || parentFolder is null)
		{
			return null;
		}

		var handles = new MemoryHandle[locators.Count];
		var childPidls = new nint[locators.Count];
		var pinnedCount = 0;
		try
		{
			for (var index = 0; index < locators.Count; index++)
			{
				handles[index] = locators[index].RelativePidl.Pin();
				childPidls[index] = (nint)handles[index].Pointer;
				pinnedCount++;
			}

			fixed (nint* childPidlPointer = childPidls)
			{
				var dataObjectId = typeof(IDataObject).GUID;
				var hr = parentFolder.GetUIObjectOf(ownerWindow, checked((uint)childPidls.Length), (ITEMIDLIST**)childPidlPointer, in dataObjectId, out var result);

				return hr.Succeeded ? result as IDataObject : null;
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
