// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;

namespace Files.Core.Browsing;

public abstract record BrowseItemChange;

public sealed record BrowseItemAdded(
	int Index,
	IStorableModel Item) : BrowseItemChange;

public sealed record BrowseItemRemoved(
	int Index,
	StorableKey Key) : BrowseItemChange;

public sealed record BrowseItemReplaced(
	int Index,
	StorableKey PreviousKey,
	IStorableModel NewItem) : BrowseItemChange;

public sealed record BrowseItemMoved(
	int PreviousIndex,
	int CurrentIndex,
	StorableKey Key) : BrowseItemChange;

public sealed record BrowseItemsReset(
	IReadOnlyList<IStorableModel> Items) : BrowseItemChange;

public sealed class BrowseItemsChangedEventArgs : EventArgs
{
	public BrowseItemsChangedEventArgs(
		long previousVersion,
		long version,
		IReadOnlyList<BrowseItemChange> changes)
	{
		ArgumentNullException.ThrowIfNull(changes);

		PreviousVersion = previousVersion;
		Version = version;
		Changes = Array.AsReadOnly(changes.ToArray());
	}

	public long PreviousVersion { get; }

	public long Version { get; }

	public IReadOnlyList<BrowseItemChange> Changes { get; }
}
