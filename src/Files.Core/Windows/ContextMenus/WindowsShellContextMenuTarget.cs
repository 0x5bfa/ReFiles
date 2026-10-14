// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains apartment-neutral item ID lists for a selection that can be bound to a classic Windows Shell context menu on the UI STA.
/// </summary>
public sealed class WindowsShellContextMenuTarget
{
	/// <summary>Gets independent absolute item ID list buffers for the selected Shell items.</summary>
	public IReadOnlyList<ReadOnlyMemory<byte>> AbsolutePidls { get; }

	internal WindowsShellContextMenuTarget(IReadOnlyList<ReadOnlyMemory<byte>> absolutePidls)
	{
		ArgumentNullException.ThrowIfNull(absolutePidls);

		AbsolutePidls = absolutePidls;
	}

	/// <summary>Resolves a complete selection to a native context-menu target.</summary>
	/// <param name="source">The source used to resolve the selection.</param>
	/// <param name="selection">All selected item references.</param>
	/// <param name="cancellationToken">Cancels selection resolution.</param>
	/// <returns>The copied target, or null when the selection has no common Shell parent.</returns>
	public static async Task<WindowsShellContextMenuTarget?> CreateAsync(WindowsStorageSource source, IReadOnlyList<Storage.StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);

		ArgumentNullException.ThrowIfNull(selection);

		var resolved = await WindowsShellMenuSelectionResolver.ResolveAsync(source, selection, cancellationToken).ConfigureAwait(false);
		if (resolved is null || !WindowsShellMenuSelectionResolver.HasCommonParent(resolved.Locators))
		{
			return null;
		}

		return new(resolved.Locators.Select(static locator => (ReadOnlyMemory<byte>)locator.AbsolutePidl.ToArray()).ToArray());
	}
}
