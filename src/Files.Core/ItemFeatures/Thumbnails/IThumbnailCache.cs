// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.ItemFeatures.Thumbnails;

/// <summary>
/// Stores materialized thumbnail payloads independently of item model lifetimes.
/// </summary>
public interface IThumbnailCache
{
	ValueTask<ThumbnailCacheEntry?> GetAsync(ThumbnailCacheKey key, CancellationToken cancellationToken = default);

	ValueTask SetAsync(ThumbnailCacheKey key, ThumbnailCacheEntry entry, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets a token that changes whenever writes for the referenced item are invalidated.
	/// </summary>
	ValueTask<long> GetInvalidationVersionAsync(StorableReference reference, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stores an entry only when no invalidation occurred after the supplied token was read.
	/// </summary>
	ValueTask<bool> TrySetAsync(ThumbnailCacheKey key, ThumbnailCacheEntry entry, long expectedInvalidationVersion, CancellationToken cancellationToken = default);

	ValueTask InvalidateAsync(StorableReference reference, CancellationToken cancellationToken = default);
}
