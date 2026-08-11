// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for memory thumbnail cache behavior.
/// </summary>
[TestClass]
public sealed class MemoryThumbnailCacheTests
{
	/// <summary>
	/// Test case: evicts the least recently used entry.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task EvictsTheLeastRecentlyUsedEntry()
	{
		var cache = new MemoryThumbnailCache(capacity: 2);
		var first = CreateKey("first", 32);
		var second = CreateKey("second", 32);
		var third = CreateKey("third", 32);

		await cache.SetAsync(first, CreateEntry("first"));
		await cache.SetAsync(second, CreateEntry("second"));
		Assert.IsNotNull(await cache.GetAsync(first));
		await cache.SetAsync(third, CreateEntry("third"));

		Assert.IsNotNull(await cache.GetAsync(first));
		Assert.IsNull(await cache.GetAsync(second));
		Assert.IsNotNull(await cache.GetAsync(third));
	}

	/// <summary>
	/// Test case: invalidate removes all sizes and modes for an item.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task InvalidateRemovesAllSizesAndModesForAnItem()
	{
		var sourceId = new StorageSourceId("test");
		var reference = new StorableReference(sourceId, "item");
		var cache = new MemoryThumbnailCache();

		await cache.SetAsync(new ThumbnailCacheKey(reference, 32, ThumbnailMode.Icon), CreateEntry("icon"));
		await cache.SetAsync(new ThumbnailCacheKey(reference, 128, ThumbnailMode.Content), CreateEntry("content"));
		await cache.SetAsync(new ThumbnailCacheKey(new StorableReference(sourceId, "other"), 32, ThumbnailMode.Icon), CreateEntry("other"));

		await cache.InvalidateAsync(reference);

		Assert.IsNull(await cache.GetAsync(new ThumbnailCacheKey(reference, 32, ThumbnailMode.Icon)));
		Assert.IsNull(await cache.GetAsync(new ThumbnailCacheKey(reference, 128, ThumbnailMode.Content)));
		Assert.IsNotNull(await cache.GetAsync(new ThumbnailCacheKey(new StorableReference(sourceId, "other"), 32, ThumbnailMode.Icon)));
	}

	/// <summary>
	/// Test case: invalidation rejects an older in flight write.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task InvalidationRejectsAnOlderInFlightWrite()
	{
		var reference = new StorableReference(new StorageSourceId("test"), "item");
		var key = new ThumbnailCacheKey(reference, 64, ThumbnailMode.Content);
		var cache = new MemoryThumbnailCache();
		var version = await cache.GetInvalidationVersionAsync(reference);

		await cache.InvalidateAsync(reference);
		var stored = await cache.TrySetAsync(key, CreateEntry("stale"), version);

		Assert.IsFalse(stored);
		Assert.IsNull(await cache.GetAsync(key));
	}

	/// <summary>
	/// Test case: wrapper does not repopulate after in flight extraction is invalidated.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WrapperDoesNotRepopulateAfterInFlightExtractionIsInvalidated()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new StorableReference(factory.Source.SourceId, coreModel.Id);
		var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var source = new TestThumbnailSource
		{
			Handler = async (_, _) =>
			{
				entered.TrySetResult(true);
				await release.Task;

				return new ThumbnailResult(new byte[] {1}, "image/png", isFallback: false);
			},
		};
		var cache = new MemoryThumbnailCache();
		var decorated = new ThumbnailCacheWrapper(cache).Wrap(new ItemContext(factory.Source, coreModel, reference), source);
		var request = new ThumbnailRequest(64, ThumbnailMode.Content);

		var extraction = decorated.GetThumbnailAsync(request).AsTask();
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await cache.InvalidateAsync(reference);
		release.TrySetResult(true);

		Assert.IsNotNull(await extraction);
		Assert.IsNull(await cache.GetAsync(new ThumbnailCacheKey(reference, 64, ThumbnailMode.Content)));
	}

	/// <summary>
	/// Test case: wrapper shares an in flight extraction.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WrapperSharesAnInFlightExtraction()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new StorableReference(factory.Source.SourceId, coreModel.Id);
		var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var source = new TestThumbnailSource
		{
			Handler = async (_, _) =>
			{
				entered.TrySetResult(true);
				await release.Task;

				return new ThumbnailResult(new byte[] {1}, "image/png", isFallback: false);
			},
		};
		var decorated = new ThumbnailCacheWrapper(new MemoryThumbnailCache()).Wrap(new ItemContext(factory.Source, coreModel, reference), source);
		var request = new ThumbnailRequest(64, ThumbnailMode.Content);

		var first = decorated.GetThumbnailAsync(request).AsTask();
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var second = decorated.GetThumbnailAsync(request).AsTask();
		release.TrySetResult(true);

		var results = await Task.WhenAll(first, second);
		Assert.AreEqual(1, source.CallCount);
		Assert.IsTrue(results.All(static result => result is not null));
	}

	/// <summary>
	/// Test case: cache entry returns an independent read only stream.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task CacheEntryReturnsAnIndependentReadOnlyStream()
	{
		var cache = new MemoryThumbnailCache();
		var key = CreateKey("item", 64);
		var bytes = new byte[] { 1, 2, 3 };
		await cache.SetAsync(key, new ThumbnailCacheEntry(bytes, "image/test"));
		bytes[0] = 99;

		var entry = await cache.GetAsync(key);
		Assert.IsNotNull(entry);
		CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, entry.Content.ToArray());
		Assert.AreEqual("image/test", entry.ContentType);
	}

	private static ThumbnailCacheKey CreateKey(string itemId, int size)
		=> new(new StorageSourceId("test"), itemId, size, ThumbnailMode.Content);

	private static ThumbnailCacheEntry CreateEntry(string value)
		=> new(System.Text.Encoding.UTF8.GetBytes(value), "text/plain");
}
