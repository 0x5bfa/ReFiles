// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using BenchmarkDotNet.Attributes;
using Files.Core.Storage;
using Files.Core.Capabilities.Thumbnails;

namespace Files.Benchmarks;

/// <summary>
/// Measures thumbnail cache hit, miss, and insertion performance.
/// </summary>
[MemoryDiagnoser]
public class ThumbnailCacheBenchmarks
{
	private MemoryThumbnailCache _cache = null!;
	private ThumbnailCacheKey _hitKey = null!;
	private ThumbnailCacheKey _missKey = null!;
	private ThumbnailCacheEntry _entry = null!;

	/// <summary>
	/// Creates and seeds the in-memory thumbnail cache.
	/// </summary>
	[GlobalSetup]
	public async Task Setup()
	{
		_cache = new MemoryThumbnailCache(capacity: 512);
		var sourceId = new StorageSourceId("benchmark");
		_hitKey = new ThumbnailCacheKey(sourceId, "hit", 128, ThumbnailMode.Content);
		_missKey = new ThumbnailCacheKey(sourceId, "miss", 128, ThumbnailMode.Content);
		_entry = new ThumbnailCacheEntry(new byte[4096], "image/test");
		await _cache.SetAsync(_hitKey, _entry);
	}

	/// <summary>
	/// Measures retrieving an existing thumbnail cache entry.
	/// </summary>
	/// <returns>A task containing the cached entry.</returns>
	[Benchmark(Baseline = true)]
	public ValueTask<ThumbnailCacheEntry?> CacheHit()
	{
		return _cache.GetAsync(_hitKey);
	}

	/// <summary>
	/// Measures retrieving a missing thumbnail cache entry.
	/// </summary>
	/// <returns>A task containing the missing lookup result.</returns>
	[Benchmark]
	public ValueTask<ThumbnailCacheEntry?> CacheMiss()
	{
		return _cache.GetAsync(_missKey);
	}

	/// <summary>
	/// Measures inserting a new entry while evicting when the cache is full.
	/// </summary>
	/// <returns>A task that represents the cache update.</returns>
	[Benchmark]
	public ValueTask CacheInsertAndEvict()
	{
		var key = new ThumbnailCacheKey(new StorageSourceId("benchmark"), Guid.NewGuid().ToString("N"), 128, ThumbnailMode.Content);

		return _cache.SetAsync(key, _entry);
	}
}
