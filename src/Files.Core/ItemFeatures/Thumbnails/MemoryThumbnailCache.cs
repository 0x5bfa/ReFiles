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

	private readonly object syncRoot = new();
	private readonly int capacity;
	private readonly Dictionary<ThumbnailCacheKey, CacheItem> items = [];
	private readonly LinkedList<ThumbnailCacheKey> usage = [];
	// Fixed stripes bound invalidation metadata. A collision may conservatively
	// reject an unrelated in-flight write, but can never admit a stale one.
	private readonly long[] invalidationVersions =
		new long[InvalidationStripeCount];

	public MemoryThumbnailCache(int capacity = 512)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
		this.capacity = capacity;
	}

	public ValueTask<ThumbnailCacheEntry?> GetAsync(
		ThumbnailCacheKey key,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(key);
		cancellationToken.ThrowIfCancellationRequested();

		lock (syncRoot)
		{
			if (!items.TryGetValue(key, out var item))
			{
				return ValueTask.FromResult<ThumbnailCacheEntry?>(null);
			}

			usage.Remove(item.UsageNode);
			usage.AddFirst(item.UsageNode);
			return ValueTask.FromResult<ThumbnailCacheEntry?>(item.Entry);
		}
	}

	public ValueTask SetAsync(
		ThumbnailCacheKey key,
		ThumbnailCacheEntry entry,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(entry);
		cancellationToken.ThrowIfCancellationRequested();

		lock (syncRoot)
		{
			SetCore(key, entry);
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask<long> GetInvalidationVersionAsync(
		StorableReference reference,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);
		cancellationToken.ThrowIfCancellationRequested();

		lock (syncRoot)
		{
			return ValueTask.FromResult(
				invalidationVersions[GetInvalidationStripe(
					reference.SourceId,
					reference.ItemId)]);
		}
	}

	public ValueTask<bool> TrySetAsync(
		ThumbnailCacheKey key,
		ThumbnailCacheEntry entry,
		long expectedInvalidationVersion,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(entry);
		ArgumentOutOfRangeException.ThrowIfNegative(expectedInvalidationVersion);
		cancellationToken.ThrowIfCancellationRequested();

		lock (syncRoot)
		{
			var stripe = GetInvalidationStripe(key.SourceId, key.ItemId);
			if (invalidationVersions[stripe]
				!= expectedInvalidationVersion)
			{
				return ValueTask.FromResult(false);
			}

			SetCore(key, entry);
			return ValueTask.FromResult(true);
		}
	}

	public ValueTask InvalidateAsync(
		StorableReference reference,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);
		cancellationToken.ThrowIfCancellationRequested();

		lock (syncRoot)
		{
			var stripe = GetInvalidationStripe(
				reference.SourceId,
				reference.ItemId);
			invalidationVersions[stripe] =
				checked(invalidationVersions[stripe] + 1);
			var keys = items.Keys
				.Where(key => key.SourceId == reference.SourceId
					&& StringComparer.Ordinal.Equals(key.ItemId, reference.ItemId))
				.ToArray();

			foreach (var key in keys)
			{
				var item = items[key];
				usage.Remove(item.UsageNode);
				items.Remove(key);
			}
		}

		return ValueTask.CompletedTask;
	}

	private void SetCore(
		ThumbnailCacheKey key,
		ThumbnailCacheEntry entry)
	{
		if (items.TryGetValue(key, out var existing))
		{
			existing.Entry = entry;
			usage.Remove(existing.UsageNode);
			usage.AddFirst(existing.UsageNode);
			return;
		}

		var usageNode = usage.AddFirst(key);
		items.Add(key, new CacheItem(entry, usageNode));

		if (items.Count > capacity && usage.Last is { } leastRecentlyUsed)
		{
			usage.RemoveLast();
			items.Remove(leastRecentlyUsed.Value);
		}
	}

	private sealed class CacheItem
	{
		public CacheItem(
			ThumbnailCacheEntry entry,
			LinkedListNode<ThumbnailCacheKey> usageNode)
		{
			Entry = entry;
			UsageNode = usageNode;
		}

		public ThumbnailCacheEntry Entry { get; set; }

		public LinkedListNode<ThumbnailCacheKey> UsageNode { get; }
	}

	private static int GetInvalidationStripe(
		StorageSourceId sourceId,
		string itemId)
	{
		return (HashCode.Combine(sourceId, itemId) & int.MaxValue)
			% InvalidationStripeCount;
	}
}
