// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.ItemFeatures.Thumbnails;

/// <summary>
/// Identifies cached content without treating a last-known address as item identity.
/// </summary>
public sealed record ThumbnailCacheKey
{

	/// <summary>Gets the storage source identifier.</summary>
	public StorageSourceId SourceId { get; }

	/// <summary>Gets the source-specific item identifier.</summary>
	public string ItemId { get; }

	/// <summary>
	/// Gets the requested bitmap edge in physical pixels.
	/// </summary>
	public int RequestedSize { get; }

	/// <summary>Gets the thumbnail selection mode.</summary>
	public ThumbnailMode Mode { get; }

	/// <summary>Initializes a thumbnail cache key.</summary>
	/// <param name="sourceId">The storage source identifier.</param>
	/// <param name="itemId">The source-specific item identifier.</param>
	/// <param name="requestedSize">The requested bitmap edge in pixels.</param>
	/// <param name="mode">The thumbnail selection mode.</param>
	public ThumbnailCacheKey(StorageSourceId sourceId, string itemId, int requestedSize, ThumbnailMode mode)
	{
		ArgumentNullException.ThrowIfNull(sourceId);
		ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedSize);

		if (mode is not ThumbnailMode.Icon and not ThumbnailMode.Content and not ThumbnailMode.PreferContent)
		{
			throw new ArgumentOutOfRangeException(nameof(mode));
		}

		SourceId = sourceId;
		ItemId = itemId;
		RequestedSize = requestedSize;
		Mode = mode;
	}

	/// <summary>Initializes a thumbnail cache key from a storage reference.</summary>
	/// <param name="reference">The storage reference.</param>
	/// <param name="requestedSize">The requested bitmap edge in pixels.</param>
	/// <param name="mode">The thumbnail selection mode.</param>
	public ThumbnailCacheKey(StorableReference reference, int requestedSize, ThumbnailMode mode)
		: this(GetReference(reference).SourceId, reference.ItemId, requestedSize, mode)
	{
	}

	private static StorableReference GetReference(StorableReference reference)
	{
		ArgumentNullException.ThrowIfNull(reference);

		return reference;
	}
}
