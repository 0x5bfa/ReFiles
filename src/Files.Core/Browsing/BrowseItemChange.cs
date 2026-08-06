// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;

namespace Files.Core.Browsing;

public abstract record BrowseItemChange;

public sealed record BrowseItemAdded(int Index, IStorableModel Item) : BrowseItemChange;

/// <summary>Describes a contiguous range of added browse items.</summary>
/// <param name="StartingIndex">The index at which the items are inserted.</param>
/// <param name="Items">The items to insert.</param>
public sealed record BrowseItemsAdded(int StartingIndex, IReadOnlyList<IStorableModel> Items) : BrowseItemChange;

public sealed record BrowseItemRemoved(int Index, StorableKey Key) : BrowseItemChange;

public sealed record BrowseItemReplaced(int Index, StorableKey PreviousKey, IStorableModel NewItem) : BrowseItemChange;

public sealed record BrowseItemMoved(int PreviousIndex, int CurrentIndex, StorableKey Key) : BrowseItemChange;

public sealed record BrowseItemsReset(IReadOnlyList<IStorableModel> Items) : BrowseItemChange;

public sealed class BrowseItemsChangedEventArgs : EventArgs
{
	public long PreviousVersion { get; }

	public long Version { get; }

	public IReadOnlyList<BrowseItemChange> Changes { get; }

	public BrowseItemsChangedEventArgs(long previousVersion, long version, IReadOnlyList<BrowseItemChange> changes)
	{
		ArgumentNullException.ThrowIfNull(changes);

		PreviousVersion = previousVersion;
		Version = version;
		Changes = Array.AsReadOnly(changes.ToArray());
	}
}
