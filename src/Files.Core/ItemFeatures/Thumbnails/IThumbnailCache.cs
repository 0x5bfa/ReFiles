// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.ItemFeatures.Thumbnails;

/// <summary>
/// Stores materialized thumbnail payloads independently of item model lifetimes.
/// </summary>
public interface IThumbnailCache
{
	/// <summary>Gets a cached thumbnail entry.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The cached entry, or <see langword="null"/> when no entry exists.</returns>
	ValueTask<ThumbnailCacheEntry?> GetAsync(ThumbnailCacheKey key, CancellationToken cancellationToken = default);

	/// <summary>Stores a thumbnail entry in the cache.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="entry">The entry to store.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask SetAsync(ThumbnailCacheKey key, ThumbnailCacheEntry entry, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets a token that changes whenever writes for the referenced item are invalidated.
	/// </summary>
	ValueTask<long> GetInvalidationVersionAsync(StorableReference reference, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stores an entry only when no invalidation occurred after the supplied token was read.
	/// </summary>
	/// <summary>Attempts to store an entry when its invalidation version is current.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="entry">The entry to store.</param>
	/// <param name="expectedInvalidationVersion">The invalidation version previously observed.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the entry was stored.</returns>
	ValueTask<bool> TrySetAsync(ThumbnailCacheKey key, ThumbnailCacheEntry entry, long expectedInvalidationVersion, CancellationToken cancellationToken = default);

	/// <summary>Invalidates entries for a storage reference.</summary>
	/// <param name="reference">The reference whose entries should be invalidated.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask InvalidateAsync(StorableReference reference, CancellationToken cancellationToken = default);
}
