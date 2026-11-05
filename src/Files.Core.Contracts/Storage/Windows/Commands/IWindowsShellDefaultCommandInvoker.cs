// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Storage.Windows;

/// <summary>Discovers and invokes an item's default Shell command.</summary>
public interface IWindowsShellDefaultCommandInvoker
{
	/// <summary>
	/// Gets the default context-menu command for an item.
	/// </summary>
	/// <param name="reference">The item to inspect.</param>
	/// <param name="cancellationToken">The token used to cancel the request.</param>
	/// <returns>The default command, or <see langword="null"/> when the context menu has no default command.</returns>
	Task<WindowsShellDefaultCommand?> GetDefaultCommandAsync(StorableReference reference, CancellationToken cancellationToken = default);

	/// <summary>
	/// Invokes the default context-menu command for an item.
	/// </summary>
	/// <param name="reference">The item to invoke.</param>
	/// <param name="context">The window and input context for the invocation.</param>
	/// <param name="cancellationToken">The token used to cancel the request.</param>
	/// <returns><see langword="true"/> when the command was invoked; otherwise, <see langword="false"/>.</returns>
	Task<bool> InvokeDefaultCommandAsync(StorableReference reference, WindowsShellInvocationContext context, CancellationToken cancellationToken = default);
}
