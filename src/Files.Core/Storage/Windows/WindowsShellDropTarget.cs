// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Contains an apartment-neutral Windows Shell destination prepared for a WinUI drop surface.
/// </summary>
public sealed class WindowsShellDropTarget
{
	private readonly WindowsItemLocator _locator;
	private readonly bool _isBackground;

	internal WindowsShellDropTarget(WindowsItemLocator locator, bool isBackground)
	{
		ArgumentNullException.ThrowIfNull(locator);

		_locator = locator;
		_isBackground = isBackground;
	}

	/// <summary>
	/// Tries to create a stateful native drop session on the calling STA.
	/// </summary>
	/// <param name="dataObject">The native data object obtained from the WinRT data view at the UI surface.</param>
	/// <param name="ownerWindowHandle">The window that owns the drop surface.</param>
	/// <param name="session">Receives a session that must be used and disposed on the creating thread.</param>
	/// <returns><see langword="true"/> when the Shell destination provided a drop target.</returns>
	public bool TryCreateSession(IDataObject dataObject, nint ownerWindowHandle, out WindowsShellDropSession? session)
	{
		ArgumentNullException.ThrowIfNull(dataObject);

		var dropTarget = _isBackground ? CreateBackgroundDropTarget((HWND)ownerWindowHandle) : CreateItemDropTarget((HWND)ownerWindowHandle);
		if (dropTarget is null)
		{
			session = null;

			return false;
		}

		session = new WindowsShellDropSession(dropTarget, dataObject);

		return true;
	}

	private IDropTarget? CreateBackgroundDropTarget(HWND ownerWindow)
	{
		var shellItem = WindowsShellItemResolver.TryCreateFromPidl(_locator.AbsolutePidl);
		if (shellItem is null || shellItem.BindToHandler<IShellFolder>(null, PInvoke.BHID_SFObject, out var folder).Failed || folder is null)
		{
			return null;
		}

		var hr = folder.CreateViewObject<IDropTarget>(ownerWindow, out var dropTarget);

		return hr.Succeeded ? dropTarget : null;
	}

	private unsafe IDropTarget? CreateItemDropTarget(HWND ownerWindow)
	{
		if (_locator.ParentFolder is { } parentLocator && !_locator.RelativePidl.IsEmpty)
		{
			var parentItem = WindowsShellItemResolver.TryCreateFromPidl(parentLocator.AbsolutePidl);
			if (parentItem is not null && parentItem.BindToHandler<IShellFolder>(null, PInvoke.BHID_SFObject, out var parentFolder).Succeeded && parentFolder is not null)
			{
				using var childHandle = _locator.RelativePidl.Pin();
				var child = (ITEMIDLIST*)childHandle.Pointer;
				var dropTargetId = typeof(IDropTarget).GUID;
				var hr = parentFolder.GetUIObjectOf(ownerWindow, 1, &child, in dropTargetId, out var result);
				if (hr.Succeeded && result is IDropTarget dropTarget)
				{
					return dropTarget;
				}
			}
		}

		var shellItem = WindowsShellItemResolver.TryCreateFromPidl(_locator.AbsolutePidl);
		if (shellItem is null)
		{
			return null;
		}

		var fallbackResult = shellItem.BindToHandler<IDropTarget>(null, PInvoke.BHID_SFUIObject, out var fallbackTarget);

		return fallbackResult.Succeeded ? fallbackTarget : null;
	}
}
