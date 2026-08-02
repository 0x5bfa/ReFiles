// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using Files.Core.ItemFeatures;

namespace Files.Core.ItemFeatures.Properties;

/// <summary>
/// Merges properties from all options, with higher-priority options winning duplicate keys.
/// </summary>
public sealed class PropertySourceCombiner : IItemFeatureCombiner<IPropertySource>
{
	public IPropertySource? Combine(ItemContext context, IReadOnlyList<ItemFeatureOption<IPropertySource>> options)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(options);

		var sources = options
			.OrderByDescending(static option => option.Priority)
			.Select(static option => option.Feature)
			.ToArray();

		return sources.Length switch
		{
			0 => null,
			1 => sources[0],
			_ => new CompositePropertySource(sources),
		};
	}

	private sealed class CompositePropertySource : IPropertySource
	{
		private readonly IReadOnlyList<IPropertySource> sources;

		public CompositePropertySource(IReadOnlyList<IPropertySource> sources)
		{
			this.sources = sources;
		}

		public async ValueTask<IReadOnlyDictionary<string, object?>> GetPropertiesAsync(
			PropertyRequest request,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);

			var merged = new Dictionary<string, object?>(StringComparer.Ordinal);

			foreach (var source in sources)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var properties = await source
					.GetPropertiesAsync(request, cancellationToken)
					.ConfigureAwait(false);

				foreach (var property in properties)
				{
					merged.TryAdd(property.Key, property.Value);
				}
			}

			return new ReadOnlyDictionary<string, object?>(merged);
		}
	}
}
