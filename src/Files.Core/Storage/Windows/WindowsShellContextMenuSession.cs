// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Owns one classic Shell context-menu session, including its native menu and owner-window message forwarding.
/// </summary>
/// <remarks>The session must be created and shown on the owning window's UI STA.</remarks>
public sealed class WindowsShellContextMenuSession
{
	private const uint ExtendedVerbs = 0x00000100;
	private const uint FirstCommandId = 1;
	private const uint InvokePointMask = 0x20000000;
	private const uint LastCommandId = 0x7FFF;
	private const uint SubclassId = 0x5246464C;
	private const uint UnicodeMask = 0x00004000;
	private const int VirtualKeyShift = 0x10;

	private IContextMenu2? _contextMenu2;
	private IContextMenu3? _contextMenu3;

	/// <summary>Shows the native popup menu and invokes the selected Shell command.</summary>
	/// <param name="owner">The owning top-level window.</param>
	/// <param name="target">The copied Shell item ID lists for the selection.</param>
	/// <param name="invocationPoint">The screen position, in physical pixels, where the menu should open.</param>
	/// <returns><see langword="true"/> when a command was selected and invoked; otherwise, <see langword="false"/>.</returns>
	public unsafe bool Show(HWND owner, WindowsShellContextMenuTarget target, Point invocationPoint)
	{
		ArgumentNullException.ThrowIfNull(target);

		if (owner.IsNull || target.AbsolutePidls.Count is 0)
		{
			return false;
		}

		var selection = CreateShellItemArray(target.AbsolutePidls);
		selection.BindToHandler<IContextMenu>(null, PInvoke.BHID_SFUIObject, out var contextMenu).ThrowOnFailure();
		using var menu = PInvoke.CreatePopupMenu_SafeHandle();
		if (menu.IsInvalid)
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError());
		}

		var queryFlags = PInvoke.GetKeyState(VirtualKeyShift) < 0 ? ExtendedVerbs : 0u;
		contextMenu.QueryContextMenu(menu, 0, FirstCommandId, LastCommandId, queryFlags).ThrowOnFailure();
		_contextMenu2 = contextMenu as IContextMenu2;
		_contextMenu3 = contextMenu as IContextMenu3;
		var sessionHandle = GCHandle.Alloc(this);
		var subclassInstalled = false;
		try
		{
			delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, nuint, nuint, LRESULT> subclassProcedure = &WindowSubclassProcedure;
			if (PInvoke.SetWindowSubclass(owner, subclassProcedure, SubclassId, (nuint)GCHandle.ToIntPtr(sessionHandle)).Value is 0)
			{
				throw new Win32Exception(Marshal.GetLastPInvokeError());
			}

			subclassInstalled = true;
			PInvoke.SetForegroundWindow(owner);
			var commandId = unchecked((uint)PInvoke.TrackPopupMenuEx(
				menu, (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON), invocationPoint.X, invocationPoint.Y, owner, null).Value);
			PInvoke.PostMessage(owner, PInvoke.WM_NULL, default, default);
			if (commandId is 0)
			{
				return false;
			}

			InvokeCommand(contextMenu, commandId - FirstCommandId, owner, invocationPoint);

			return true;
		}
		finally
		{
			if (subclassInstalled)
			{
				delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, nuint, nuint, LRESULT> subclassProcedure = &WindowSubclassProcedure;
				PInvoke.RemoveWindowSubclass(owner, subclassProcedure, SubclassId);
			}

			sessionHandle.Free();
			_contextMenu2 = null;
			_contextMenu3 = null;
		}
	}

	private static unsafe IShellItemArray CreateShellItemArray(IReadOnlyList<ReadOnlyMemory<byte>> absolutePidls)
	{
		var handles = new MemoryHandle[absolutePidls.Count];
		var itemIdLists = new nint[absolutePidls.Count];
		var pinnedCount = 0;
		try
		{
			for (var index = 0; index < absolutePidls.Count; index++)
			{
				if (absolutePidls[index].IsEmpty)
				{
					throw new InvalidOperationException("A Windows Shell context-menu item does not have an absolute item ID list.");
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

	private static unsafe void InvokeCommand(IContextMenu contextMenu, uint commandOrdinal, HWND owner, Point invocationPoint)
	{
		var invoke = new CMINVOKECOMMANDINFOEX
		{
			cbSize = (uint)sizeof(CMINVOKECOMMANDINFOEX),
			fMask = UnicodeMask | InvokePointMask,
			hwnd = owner,
			lpVerb = (PCSTR)(byte*)(nuint)commandOrdinal,
			nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL,
			ptInvoke = invocationPoint,
		};
		ref var baseInvoke = ref Unsafe.As<CMINVOKECOMMANDINFOEX, CMINVOKECOMMANDINFO>(ref invoke);
		contextMenu.InvokeCommand(baseInvoke).ThrowOnFailure();
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static LRESULT WindowSubclassProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam, nuint subclassId, nuint referenceData)
	{
		var sessionHandle = GCHandle.FromIntPtr((nint)referenceData);

		return sessionHandle.Target is WindowsShellContextMenuSession session
			? session.HandleWindowMessage(window, message, wParam, lParam)
			: PInvoke.DefSubclassProc(window, message, wParam, lParam);
	}

	private unsafe LRESULT HandleWindowMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
	{
		if (message is PInvoke.WM_INITMENUPOPUP or PInvoke.WM_DRAWITEM or PInvoke.WM_MEASUREITEM or PInvoke.WM_MENUCHAR)
		{
			if (_contextMenu3 is not null)
			{
				var result = default(LRESULT);
				if (_contextMenu3.HandleMenuMsg2(message, wParam, lParam, &result).Succeeded)
				{
					return result;
				}
			}

			if (_contextMenu2 is not null && _contextMenu2.HandleMenuMsg(message, wParam, lParam).Succeeded)
			{
				return default;
			}
		}

		return PInvoke.DefSubclassProc(window, message, wParam, lParam);
	}
}
