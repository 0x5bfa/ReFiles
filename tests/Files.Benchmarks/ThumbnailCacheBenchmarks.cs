// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using BenchmarkDotNet.Attributes;
using Files.Core.Storage;
using Files.Core.ItemFeatures.Thumbnails;

namespace Files.Benchmarks;

[MemoryDiagnoser]
public class ThumbnailCacheBenchmarks
{
	private MemoryThumbnailCache cache = null!;
	private ThumbnailCacheKey hitKey = null!;
	private ThumbnailCacheKey missKey = null!;
	private ThumbnailCacheEntry entry = null!;

	[GlobalSetup]
	public async Task Setup()
	{
		cache = new MemoryThumbnailCache(capacity: 512);
		var sourceId = new StorageSourceId("benchmark");
		hitKey = new ThumbnailCacheKey(sourceId, "hit", 128, ThumbnailMode.Content);
		missKey = new ThumbnailCacheKey(sourceId, "miss", 128, ThumbnailMode.Content);
		entry = new ThumbnailCacheEntry(new byte[4096], "image/test");
		await cache.SetAsync(hitKey, entry);
	}

	[Benchmark(Baseline = true)]
	public ValueTask<ThumbnailCacheEntry?> CacheHit() => cache.GetAsync(hitKey);

	[Benchmark]
	public ValueTask<ThumbnailCacheEntry?> CacheMiss() => cache.GetAsync(missKey);

	[Benchmark]
	public ValueTask CacheInsertAndEvict()
	{
		var key = new ThumbnailCacheKey(new StorageSourceId("benchmark"), Guid.NewGuid().ToString("N"), 128, ThumbnailMode.Content);
		return cache.SetAsync(key, entry);
	}
}
