// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using Files.Core.ItemFeatures;
using Files.Core.Storage;

namespace Files.Core.ItemFeatures.Thumbnails;

/// <summary>
/// Wraps a composed thumbnail source with a shared, composition-root-owned cache.
/// </summary>
public sealed class ThumbnailCacheWrapper : IItemFeatureWrapper<IThumbnailSource>
{
	private readonly IThumbnailCache cache;
	private readonly ConcurrentDictionary<ThumbnailCacheKey, Lazy<Task<ThumbnailResult?>>> inFlight = [];

	public ThumbnailCacheWrapper(IThumbnailCache cache)
	{
		ArgumentNullException.ThrowIfNull(cache);
		this.cache = cache;
	}

	public IThumbnailSource Wrap(ItemContext context, IThumbnailSource source)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(source);

		return new CachedThumbnailSource(context.Reference, source, cache, inFlight);
	}

	private sealed class CachedThumbnailSource : IThumbnailSource
	{
		private readonly StorableReference reference;
		private readonly IThumbnailSource innerSource;
		private readonly IThumbnailCache cache;
		private readonly ConcurrentDictionary<ThumbnailCacheKey, Lazy<Task<ThumbnailResult?>>> inFlight;

		public CachedThumbnailSource(
			StorableReference reference,
			IThumbnailSource innerSource,
			IThumbnailCache cache,
			ConcurrentDictionary<
				ThumbnailCacheKey,
				Lazy<Task<ThumbnailResult?>>> inFlight)
		{
			this.reference = reference;
			this.innerSource = innerSource;
			this.cache = cache;
			this.inFlight = inFlight;
		}

		public async ValueTask<ThumbnailResult?> GetThumbnailAsync(ThumbnailRequest request, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);

			var key = new ThumbnailCacheKey(reference, request.RequestedPixelSize, request.Mode);
			var cached = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);

			if (cached is not null)
			{
				return cached.CreateResult();
			}

			var lazy = new Lazy<Task<ThumbnailResult?>>(() => LoadAndCacheAsync(key, request), LazyThreadSafetyMode.ExecutionAndPublication);
			var selected = inFlight.GetOrAdd(key, lazy);
			if (ReferenceEquals(selected, lazy))
			{
				_ = selected.Value.ContinueWith(
					completed =>
					{
						_ = completed.Exception;
						RemoveInFlight(key, selected);
					},
					CancellationToken.None,
					TaskContinuationOptions.ExecuteSynchronously
					| TaskContinuationOptions.DenyChildAttach,
					TaskScheduler.Default);
			}

			try
			{
				return await selected.Value
					.WaitAsync(cancellationToken)
					.ConfigureAwait(false);
			}
			finally
			{
				if (selected.IsValueCreated && selected.Value.IsCompleted)
				{
					RemoveInFlight(key, selected);
				}
			}
		}

		private async Task<ThumbnailResult?> LoadAndCacheAsync(ThumbnailCacheKey key, ThumbnailRequest request)
		{
			var sharedOperation = CancellationToken.None;
			var invalidationVersion = await cache
				.GetInvalidationVersionAsync(reference, sharedOperation)
				.ConfigureAwait(false);
			var cached = await cache
				.GetAsync(key, sharedOperation)
				.ConfigureAwait(false);
			if (cached is not null)
			{
				return cached.CreateResult();
			}

			var result = await innerSource
				.GetThumbnailAsync(request, sharedOperation)
				.ConfigureAwait(false);
			if (result is null)
			{
				return null;
			}

			var entry = new ThumbnailCacheEntry(result.Content.ToArray(), result.ContentType, result.IsFallback);
			await cache
				.TrySetAsync(key, entry, invalidationVersion, sharedOperation)
				.ConfigureAwait(false);
			return entry.CreateResult();
		}

		private void RemoveInFlight(ThumbnailCacheKey key, Lazy<Task<ThumbnailResult?>> selected)
		{
			((ICollection<
				KeyValuePair<
					ThumbnailCacheKey,
					Lazy<Task<ThumbnailResult?>>>>)inFlight)
				.Remove(new KeyValuePair<
					ThumbnailCacheKey,
					Lazy<Task<ThumbnailResult?>>>(key, selected));
		}
	}
}
