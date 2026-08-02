// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Stable storage-source identity and an optional recovery locator for an item.
/// </summary>
public sealed record StorableReference
{
	public StorageSourceId SourceId { get; }

	public string ItemId { get; }

	/// <summary>
	/// Gets a mutable-in-the-world recovery hint. It is intentionally excluded
	/// from equality and hashing.
	/// </summary>
	public StorageAddress? LastKnownAddress { get; }

	public StorableReference(StorageSourceId sourceId, string itemId, StorageAddress? lastKnownAddress = null)
	{
		ArgumentNullException.ThrowIfNull(sourceId);
		ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

		SourceId = sourceId;
		ItemId = itemId;
		LastKnownAddress = lastKnownAddress;
	}

	public bool Equals(StorableReference? other)
	{
		return other is not null
			&& SourceId == other.SourceId
			&& StringComparer.Ordinal.Equals(ItemId, other.ItemId);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(SourceId, StringComparer.Ordinal.GetHashCode(ItemId));
	}
}
