// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.ItemFeatures.Thumbnails;

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

	public MemoryThumbnailCache(int capacity = 512)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

		_capacity = capacity;
	}

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

	public ValueTask<long> GetInvalidationVersionAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);

		cancellationToken.ThrowIfCancellationRequested();

		lock (_syncRoot)
		{
			return ValueTask.FromResult(_invalidationVersions[GetInvalidationStripe(reference.SourceId, reference.ItemId)]);
		}
	}

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
