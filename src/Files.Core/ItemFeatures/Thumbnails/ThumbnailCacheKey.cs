// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.ItemFeatures.Thumbnails;

/// <summary>
/// Identifies cached content without treating a last-known address as item identity.
/// </summary>
public sealed record ThumbnailCacheKey
{
	public ThumbnailCacheKey(
		StorageSourceId sourceId,
		string itemId,
		int requestedSize,
		ThumbnailMode mode)
	{
		ArgumentNullException.ThrowIfNull(sourceId);
		ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedSize);
		if (mode is not ThumbnailMode.Icon
			and not ThumbnailMode.Content
			and not ThumbnailMode.PreferContent)
		{
			throw new ArgumentOutOfRangeException(nameof(mode));
		}

		SourceId = sourceId;
		ItemId = itemId;
		RequestedSize = requestedSize;
		Mode = mode;
	}

	public ThumbnailCacheKey(
		StorableReference reference,
		int requestedSize,
		ThumbnailMode mode)
		: this(
			GetReference(reference).SourceId,
			reference.ItemId,
			requestedSize,
			mode)
	{
	}

	public StorageSourceId SourceId { get; }

	public string ItemId { get; }

	/// <summary>
	/// Gets the requested bitmap edge in physical pixels.
	/// </summary>
	public int RequestedSize { get; }

	public ThumbnailMode Mode { get; }

	private static StorableReference GetReference(StorableReference reference)
	{
		ArgumentNullException.ThrowIfNull(reference);
		return reference;
	}
}
