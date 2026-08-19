// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Resolves and invokes the default context-menu command for Windows Shell items.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsShellDefaultCommandInvoker
{
	private const uint FirstCommandId = 1;
	private const uint LastCommandId = 0x7FFF;
	private const uint NoDefaultCommand = uint.MaxValue;
	private const uint CanonicalVerbUnicode = 0x00000004;
	// CDefView adds EXPLORE; the Shell invocation helpers add DEFAULTONLY and OPTIMIZEFORINVOKE.
	private const uint ExplorerDefaultQueryFlags = 0x00000001 | 0x00000004 | 0x00000800;
	// Explorer requests synchronous Unicode invocation and records the item launch.
	private const uint ExplorerDefaultInvokeMask = 0x00000100 | 0x00004000 | 0x04000000;
	private const uint InvokePointMask = 0x20000000;
	private const int CanonicalVerbBufferLength = 128;

	private readonly WindowsStorageSource _source;

	/// <summary>
	/// Initializes a Windows Shell default-command invoker.
	/// </summary>
	/// <param name="source">The Windows storage source used to resolve Shell items.</param>
	public WindowsShellDefaultCommandInvoker(WindowsStorageSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		_source = source;
	}

	/// <summary>
	/// Gets the default context-menu command for an item.
	/// </summary>
	/// <param name="reference">The item to inspect.</param>
	/// <param name="cancellationToken">The token used to cancel the request.</param>
	/// <returns>The default command, or <see langword="null"/> when the context menu has no default command.</returns>
	public async Task<WindowsShellDefaultCommand?> GetDefaultCommandAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);

		var storable = await _source.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
		if (storable is not WindowsStorable windowsStorable)
		{
			throw new InvalidOperationException("The resolved item is not backed by the Windows Shell.");
		}

		return await _source.ShellItemResolver.InvokeOperationAsync(windowsStorable.Locator, GetDefaultCommandOnSta, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Invokes the default context-menu command for an item.
	/// </summary>
	/// <param name="reference">The item to invoke.</param>
	/// <param name="context">The window and input context for the invocation.</param>
	/// <param name="cancellationToken">The token used to cancel the request.</param>
	/// <returns><see langword="true"/> when the command was invoked; otherwise, <see langword="false"/>.</returns>
	public async Task<bool> InvokeDefaultCommandAsync(StorableReference reference, WindowsShellInvocationContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);
		ArgumentNullException.ThrowIfNull(context);

		var storable = await _source.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
		if (storable is not WindowsStorable windowsStorable)
		{
			throw new InvalidOperationException("The resolved item is not backed by the Windows Shell.");
		}

		return await _source.ShellItemResolver.InvokeOperationAsync(windowsStorable.Locator, shellItem => InvokeDefaultCommandOnSta(shellItem, context), cancellationToken).ConfigureAwait(false);
	}

	private static WindowsShellDefaultCommand? GetDefaultCommandOnSta(IShellItem shellItem)
	{
		var contextMenu = CreateContextMenu(shellItem);
		using var menu = CreateMenu();
		contextMenu.QueryContextMenu(menu, 0, FirstCommandId, LastCommandId, ExplorerDefaultQueryFlags).ThrowOnFailure();
		var commandId = PInvoke.GetMenuDefaultItem(menu, 0, 0);
		if (commandId is NoDefaultCommand)
		{
			return null;
		}

		var commandOrdinal = commandId - FirstCommandId;

		return new WindowsShellDefaultCommand(GetCanonicalVerb(contextMenu, commandOrdinal));
	}

	private static bool InvokeDefaultCommandOnSta(IShellItem shellItem, WindowsShellInvocationContext context)
	{
		var contextMenu = CreateContextMenu(shellItem);
		using var menu = CreateMenu();
		contextMenu.QueryContextMenu(menu, 0, FirstCommandId, LastCommandId, ExplorerDefaultQueryFlags).ThrowOnFailure();
		var commandId = PInvoke.GetMenuDefaultItem(menu, 0, 0);
		if (commandId is NoDefaultCommand)
		{
			return false;
		}

		InvokeCommand(contextMenu, commandId - FirstCommandId, context);

		return true;
	}

	private static IContextMenu CreateContextMenu(IShellItem shellItem)
	{
		PInvoke.SHCreateShellItemArrayFromShellItem(shellItem, out IShellItemArray selection).ThrowOnFailure();
		selection.BindToHandler<IContextMenu>(null, PInvoke.BHID_SFUIObject, out var contextMenu).ThrowOnFailure();

		return contextMenu;
	}

	private static DestroyMenuSafeHandle CreateMenu()
	{
		var menu = PInvoke.CreatePopupMenu_SafeHandle();
		if (menu.IsInvalid)
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError());
		}

		return menu;
	}

	private static unsafe string? GetCanonicalVerb(IContextMenu contextMenu, uint commandOrdinal)
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

	private static unsafe void InvokeCommand(IContextMenu contextMenu, uint commandOrdinal, WindowsShellInvocationContext context)
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
					fMask = ExplorerDefaultInvokeMask,
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
