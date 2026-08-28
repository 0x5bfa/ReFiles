// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Capabilities.Thumbnails;

/// <summary>
/// Provides a bounded process-memory LRU cache for thumbnail wrappers.
/// </summary>
public sealed class MemoryThumbnailCache : IThumbnailCache
{
	private const int InvalidationStripeCount = 64;

	private readonly Lock _syncRoot = new();
	private readonly int _capacity;
	private readonly Dictionary<ThumbnailCacheKey, CacheItem> _items = [];
	private readonly LinkedList<ThumbnailCacheKey> _usage = [];
	// Fixed stripes bound invalidation metadata. A collision may conservatively
	// reject an unrelated in-flight write, but can never admit a stale one.
	private readonly long[] _invalidationVersions = new long[InvalidationStripeCount];

	/// <summary>Initializes a bounded in-memory thumbnail cache.</summary>
	/// <param name="capacity">The maximum number of entries to retain.</param>
	public MemoryThumbnailCache(int capacity = 512)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

		_capacity = capacity;
	}

	/// <summary>Gets an entry from the cache.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The cached entry, or <see langword="null"/> when no entry exists.</returns>
	public ValueTask<ThumbnailCacheEntry?> GetAsync(ThumbnailCacheKey key, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(key);

		cancellationToken.ThrowIfCancellationRequested();

		lock (_syncRoot)
		{
			if (!_items.TryGetValue(key, out var item))
			{
				return ValueTask.FromResult<ThumbnailCacheEntry?>(null);
			}

			_usage.Remove(item.UsageNode);
			_usage.AddFirst(item.UsageNode);

			return ValueTask.FromResult<ThumbnailCacheEntry?>(item.Entry);
		}
	}

	/// <summary>Stores an entry in the cache.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="entry">The entry to store.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	public ValueTask SetAsync(ThumbnailCacheKey key, ThumbnailCacheEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(entry);

		cancellationToken.ThrowIfCancellationRequested();

		lock (_syncRoot)
		{
			SetCore(key, entry);
		}

		return ValueTask.CompletedTask;
	}

	/// <summary>Gets the invalidation version for a storage reference.</summary>
	/// <param name="reference">The storage reference.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The current invalidation version.</returns>
	public ValueTask<long> GetInvalidationVersionAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);

		cancellationToken.ThrowIfCancellationRequested();

		lock (_syncRoot)
		{
			return ValueTask.FromResult(_invalidationVersions[GetInvalidationStripe(reference.SourceId, reference.ItemId)]);
		}
	}

	/// <summary>Stores an entry if its invalidation version is still current.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="entry">The entry to store.</param>
	/// <param name="expectedInvalidationVersion">The version observed before producing the entry.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the entry was stored.</returns>
	public ValueTask<bool> TrySetAsync(ThumbnailCacheKey key, ThumbnailCacheEntry entry, long expectedInvalidationVersion, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(entry);
		ArgumentOutOfRangeException.ThrowIfNegative(expectedInvalidationVersion);

		cancellationToken.ThrowIfCancellationRequested();

		lock (_syncRoot)
		{
			var stripe = GetInvalidationStripe(key.SourceId, key.ItemId);
			if (_invalidationVersions[stripe] != expectedInvalidationVersion)
			{
				return ValueTask.FromResult(false);
			}

			SetCore(key, entry);

			return ValueTask.FromResult(true);
		}
	}

	/// <summary>Invalidates cached entries for a storage reference.</summary>
	/// <param name="reference">The storage reference.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	public ValueTask InvalidateAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);

		cancellationToken.ThrowIfCancellationRequested();

		lock (_syncRoot)
		{
			var stripe = GetInvalidationStripe(reference.SourceId, reference.ItemId);
			_invalidationVersions[stripe] =
				checked(_invalidationVersions[stripe] + 1);
			var keys = _items.Keys.Where(key => key.SourceId == reference.SourceId && StringComparer.Ordinal.Equals(key.ItemId, reference.ItemId)).ToArray();

			foreach (var key in keys)
			{
				var item = _items[key];
				_usage.Remove(item.UsageNode);
				_items.Remove(key);
			}
		}

		return ValueTask.CompletedTask;
	}

	private void SetCore(ThumbnailCacheKey key, ThumbnailCacheEntry entry)
	{
		if (_items.TryGetValue(key, out var existing))
		{
			existing.Entry = entry;
			_usage.Remove(existing.UsageNode);
			_usage.AddFirst(existing.UsageNode);

			return;
		}

		var usageNode = _usage.AddFirst(key);
		_items.Add(key, new CacheItem(entry, usageNode));

		if (_items.Count > _capacity && _usage.Last is { } leastRecentlyUsed)
		{
			_usage.RemoveLast();
			_items.Remove(leastRecentlyUsed.Value);
		}
	}

	private sealed class CacheItem
	{
		public ThumbnailCacheEntry Entry { get; set; }

		public LinkedListNode<ThumbnailCacheKey> UsageNode { get; }

		public CacheItem(ThumbnailCacheEntry entry, LinkedListNode<ThumbnailCacheKey> usageNode)
		{
			Entry = entry;
			UsageNode = usageNode;
		}
	}

	private static int GetInvalidationStripe(StorageSourceId sourceId, string itemId)
	{
		return (HashCode.Combine(sourceId, itemId) & int.MaxValue)
			% InvalidationStripeCount;
	}
}
