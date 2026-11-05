// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;

namespace Files.Core.Browsing;

/// <summary>Describes a change to the items in a browse session.</summary>
public abstract record BrowseItemChange;

/// <summary>Describes an item inserted at a specific index.</summary>
/// <param name="Index">The index where the item was inserted.</param>
/// <param name="Item">The inserted item.</param>
public sealed record BrowseItemAdded(int Index, IStorableModel Item) : BrowseItemChange;

/// <summary>Describes a contiguous range of added browse items.</summary>
/// <param name="StartingIndex">The index at which the items are inserted.</param>
/// <param name="Items">The items to insert.</param>
public sealed record BrowseItemsAdded(int StartingIndex, IReadOnlyList<IStorableModel> Items) : BrowseItemChange;

/// <summary>Describes an item removed from a specific index.</summary>
/// <param name="Index">The index from which the item was removed.</param>
/// <param name="Key">The key of the removed item.</param>
public sealed record BrowseItemRemoved(int Index, StorableKey Key) : BrowseItemChange;

/// <summary>Describes an item replaced at a specific index.</summary>
/// <param name="Index">The index of the replaced item.</param>
/// <param name="PreviousKey">The key of the item before replacement.</param>
/// <param name="NewItem">The replacement item.</param>
public sealed record BrowseItemReplaced(int Index, StorableKey PreviousKey, IStorableModel NewItem) : BrowseItemChange;

/// <summary>Describes an item moved to a different index.</summary>
/// <param name="PreviousIndex">The item's original index.</param>
/// <param name="CurrentIndex">The item's new index.</param>
/// <param name="Key">The key of the moved item.</param>
public sealed record BrowseItemMoved(int PreviousIndex, int CurrentIndex, StorableKey Key) : BrowseItemChange;

/// <summary>Describes replacing the complete browse item set.</summary>
/// <param name="Items">The complete item set after the reset.</param>
public sealed record BrowseItemsReset(IReadOnlyList<IStorableModel> Items) : BrowseItemChange;

/// <summary>Provides the version and item changes published by a browse session.</summary>
public sealed class BrowseItemsChangedEventArgs : EventArgs
{
	/// <summary>Gets the item version before the changes were applied.</summary>
	public long PreviousVersion { get; }

	/// <summary>Gets the item version after the changes were applied.</summary>
	public long Version { get; }

	/// <summary>Gets the ordered changes included in the update.</summary>
	public IReadOnlyList<BrowseItemChange> Changes { get; }

	/// <summary>Initializes event data for a browse item update.</summary>
	/// <param name="previousVersion">The item version before the update.</param>
	/// <param name="version">The item version after the update.</param>
	/// <param name="changes">The changes included in the update.</param>
	public BrowseItemsChangedEventArgs(long previousVersion, long version, IReadOnlyList<BrowseItemChange> changes)
	{
		ArgumentNullException.ThrowIfNull(changes);

		PreviousVersion = previousVersion;
		Version = version;
		Changes = Array.AsReadOnly(changes.ToArray());
	}
}
