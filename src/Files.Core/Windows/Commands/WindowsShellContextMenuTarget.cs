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
}
