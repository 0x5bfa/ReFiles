// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;

namespace Files.Core.Capabilities.Previews;

/// <summary>
/// Builds a priority-ordered preview router from all preview options.
/// </summary>
public sealed class PreviewSourceCombiner : ICapabilityCombiner<IPreviewSource>
{
	/// <summary>Combines preview sources in descending priority order.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="options">The preview source options.</param>
	/// <returns>A routed source, or <see langword="null"/> when no source applies.</returns>
	public IPreviewSource? Combine(ItemContext context, IReadOnlyList<CapabilityOption<IPreviewSource>> options)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(options);

		var sources = options.OrderByDescending(static option => option.Priority).Select(static option => option.Capability).ToArray();

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
