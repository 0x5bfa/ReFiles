// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.Versioning;
using Files.Core.Storage;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

/// <summary>
/// Resolves and invokes the default context-menu command for Windows Shell items.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsShellDefaultCommandInvoker
{
	private const uint NoDefaultCommand = uint.MaxValue;
	// CDefView adds EXPLORE; the Shell invocation helpers add DEFAULTONLY and OPTIMIZEFORINVOKE.
	private const uint ExplorerDefaultQueryFlags = 0x00000001 | 0x00000004 | 0x00000800;

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
		var contextMenu = WindowsShellContextMenuCommandHelper.Create(shellItem);
		using var menu = WindowsShellContextMenuCommandHelper.CreateMenu();
		contextMenu.QueryContextMenu(menu, 0, WindowsShellContextMenuCommandHelper.FirstCommandId, WindowsShellContextMenuCommandHelper.LastCommandId, ExplorerDefaultQueryFlags).ThrowOnFailure();
		var commandId = PInvoke.GetMenuDefaultItem(menu, 0, 0);
		if (commandId is NoDefaultCommand)
		{
			return null;
		}

		var commandOrdinal = commandId - WindowsShellContextMenuCommandHelper.FirstCommandId;

		return new WindowsShellDefaultCommand(WindowsShellContextMenuCommandHelper.GetCanonicalVerb(contextMenu, commandOrdinal));
	}

	private static bool InvokeDefaultCommandOnSta(IShellItem shellItem, WindowsShellInvocationContext context)
	{
		var contextMenu = WindowsShellContextMenuCommandHelper.Create(shellItem);
		using var menu = WindowsShellContextMenuCommandHelper.CreateMenu();
		contextMenu.QueryContextMenu(menu, 0, WindowsShellContextMenuCommandHelper.FirstCommandId, WindowsShellContextMenuCommandHelper.LastCommandId, ExplorerDefaultQueryFlags).ThrowOnFailure();
		var commandId = PInvoke.GetMenuDefaultItem(menu, 0, 0);
		if (commandId is NoDefaultCommand)
		{
			return false;
		}

		WindowsShellContextMenuCommandHelper.Invoke(contextMenu, commandId - WindowsShellContextMenuCommandHelper.FirstCommandId, context);

		return true;
	}
}
