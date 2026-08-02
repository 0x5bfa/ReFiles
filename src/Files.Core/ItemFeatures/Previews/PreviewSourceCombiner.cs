// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;

namespace Files.Core.ItemFeatures.Previews;

/// <summary>
/// Builds a priority-ordered preview router from all preview options.
/// </summary>
public sealed class PreviewSourceCombiner : IItemFeatureCombiner<IPreviewSource>
{
	public IPreviewSource? Combine(ItemContext context, IReadOnlyList<ItemFeatureOption<IPreviewSource>> options)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(options);

		var sources = options.OrderByDescending(static option => option.Priority).Select(static option => option.Feature).ToArray();

		return sources.Length switch
		{
			0 => null,
			1 => sources[0],
			_ => new RoutedPreviewSource(sources),
		};
	}

	private sealed class RoutedPreviewSource : IPreviewSource
	{
		private readonly IReadOnlyList<IPreviewSource> _sources;

		public RoutedPreviewSource(IReadOnlyList<IPreviewSource> sources)
		{
			_sources = sources;
		}

		public async ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);

			foreach (var source in _sources)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var result = await source.GetPreviewAsync(request, cancellationToken).ConfigureAwait(false);

				if (result is not null)
				{
					return result;
				}
			}

			return null;
		}
	}
}
