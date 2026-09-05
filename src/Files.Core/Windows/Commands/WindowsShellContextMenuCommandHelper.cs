// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Windows;

internal static class WindowsShellContextMenuCommandHelper
{
	internal const uint FirstCommandId = 1;
	internal const uint LastCommandId = 0x7FFF;
	internal const uint CanonicalVerbUnicode = 0x00000004;
	internal const uint ExplorerInvokeMask = 0x00000100 | 0x00004000 | 0x04000000;
	internal const uint InvokePointMask = 0x20000000;
	private const int CanonicalVerbBufferLength = 128;

	internal static IContextMenu Create(IShellItem shellItem)
	{
		PInvoke.SHCreateShellItemArrayFromShellItem(shellItem, out IShellItemArray selection).ThrowOnFailure();

		return Create(selection);
	}

	internal static IContextMenu Create(IShellItemArray selection)
	{
		selection.BindToHandler<IContextMenu>(null, PInvoke.BHID_SFUIObject, out var contextMenu).ThrowOnFailure();

		return contextMenu;
	}

	internal static DestroyMenuSafeHandle CreateMenu()
	{
		var menu = PInvoke.CreatePopupMenu_SafeHandle();
		if (menu.IsInvalid)
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError());
		}

		return menu;
	}

	internal static unsafe string? GetCanonicalVerb(IContextMenu contextMenu, uint commandOrdinal)
	{
		Span<char> buffer = stackalloc char[CanonicalVerbBufferLength];
		fixed (char* bufferPointer = buffer)
		{
			var result = contextMenu.GetCommandString(commandOrdinal, CanonicalVerbUnicode, (PSTR)(byte*)bufferPointer, (uint)buffer.Length);
			if (result.Failed)
			{
				return null;
			}
		}

		var terminatorIndex = buffer.IndexOf('\0');
		var verb = new string(terminatorIndex < 0 ? buffer : buffer[..terminatorIndex]);

		return string.IsNullOrWhiteSpace(verb) ? null : verb;
	}

	internal static unsafe void Invoke(IContextMenu contextMenu, uint commandOrdinal, WindowsShellInvocationContext context)
	{
		var workingDirectory = context.WorkingDirectory;
		var ansiWorkingDirectory = workingDirectory is null ? 0 : Marshal.StringToCoTaskMemAnsi(workingDirectory);

		try
		{
			fixed (char* workingDirectoryPointer = workingDirectory)
			{
				var invoke = new CMINVOKECOMMANDINFOEX
				{
					cbSize = (uint)sizeof(CMINVOKECOMMANDINFOEX),
					fMask = ExplorerInvokeMask,
					hwnd = (HWND)context.OwnerWindowHandle,
					lpVerb = (PCSTR)(byte*)(nuint)commandOrdinal,
					lpDirectory = (PCSTR)(byte*)ansiWorkingDirectory,
					nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL,
					lpDirectoryW = workingDirectoryPointer,
				};

				if (context.InvocationPoint is { } invocationPoint)
				{
					invoke.fMask |= InvokePointMask;
					invoke.ptInvoke = new(invocationPoint.X, invocationPoint.Y);
				}

				ref var baseInvoke = ref Unsafe.As<CMINVOKECOMMANDINFOEX, CMINVOKECOMMANDINFO>(ref invoke);
				contextMenu.InvokeCommand(baseInvoke).ThrowOnFailure();
			}
		}
		finally
		{
			if (ansiWorkingDirectory is not 0)
			{
				Marshal.FreeCoTaskMem(ansiWorkingDirectory);
			}
		}
	}
}
