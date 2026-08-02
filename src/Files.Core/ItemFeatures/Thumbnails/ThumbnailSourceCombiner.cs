// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;

namespace Files.Core.ItemFeatures.Thumbnails;

/// <summary>
/// Builds a priority-ordered fallback chain from all thumbnail options.
/// </summary>
public sealed class ThumbnailSourceCombiner : IItemFeatureCombiner<IThumbnailSource>
{
	public IThumbnailSource? Combine(ItemContext context, IReadOnlyList<ItemFeatureOption<IThumbnailSource>> options)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(options);

		var sources = options.OrderByDescending(static option => option.Priority).Select(static option => option.Feature).ToArray();

		return sources.Length switch
		{
			0 => null,
			1 => sources[0],
			_ => new FallbackThumbnailSource(sources),
		};
	}

	private sealed class FallbackThumbnailSource : IThumbnailSource
	{
		private readonly IReadOnlyList<IThumbnailSource> _sources;

		public FallbackThumbnailSource(IReadOnlyList<IThumbnailSource> sources)
		{
			_sources = sources;
		}

		public async ValueTask<ThumbnailResult?> GetThumbnailAsync(ThumbnailRequest request, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);

			foreach (var source in _sources)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var result = await source.GetThumbnailAsync(request, cancellationToken).ConfigureAwait(false);
				if (result is not null)
				{
					return result;
				}
			}

			return null;
		}
	}
}
