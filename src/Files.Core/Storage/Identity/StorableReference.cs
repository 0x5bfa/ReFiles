// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Stable storage-source identity and an optional recovery locator for an item.
/// </summary>
public sealed record StorableReference
{
	/// <summary>Gets the storage source identifier.</summary>
	public StorageSourceId SourceId { get; }

	/// <summary>Gets the source-specific item identifier.</summary>
	public string ItemId { get; }

	/// <summary>
	/// Gets a mutable-in-the-world recovery hint. It is intentionally excluded
	/// from equality and hashing.
	/// </summary>
	public StorageAddress? LastKnownAddress { get; }

	/// <summary>Initializes a stable storage reference.</summary>
	/// <param name="sourceId">The storage source identifier.</param>
	/// <param name="itemId">The source-specific item identifier.</param>
	/// <param name="lastKnownAddress">An optional recovery address.</param>
	public StorableReference(StorageSourceId sourceId, string itemId, StorageAddress? lastKnownAddress = null)
	{
		ArgumentNullException.ThrowIfNull(sourceId);
		ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

		SourceId = sourceId;
		ItemId = itemId;
		LastKnownAddress = lastKnownAddress;
	}

	/// <summary>Determines whether another reference identifies the same item.</summary>
	/// <param name="other">The reference to compare.</param>
	/// <returns><see langword="true"/> when both references identify the same item.</returns>
	public bool Equals(StorableReference? other)
	{
		return other is not null
			&& SourceId == other.SourceId
			&& StringComparer.Ordinal.Equals(ItemId, other.ItemId);
	}

	/// <summary>Gets a hash code based on source and item identity.</summary>
	/// <returns>The identity hash code.</returns>
	public override int GetHashCode()
	{
		return HashCode.Combine(SourceId, StringComparer.Ordinal.GetHashCode(ItemId));
	}
}
