// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using Files.Core.Capabilities;

namespace Files.Core.Capabilities.Properties;

/// <summary>
/// Merges properties from all options, with higher-priority options winning duplicate keys.
/// </summary>
public sealed class PropertySourceCombiner : ICapabilityCombiner<IPropertySource>
{
	/// <summary>Combines property sources in descending priority order.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="options">The property source options.</param>
	/// <returns>A combined property source, or <see langword="null"/> when no source applies.</returns>
	public IPropertySource? Combine(ItemContext context, IReadOnlyList<CapabilityOption<IPropertySource>> options)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(options);

		var sources = options.OrderByDescending(static option => option.Priority).Select(static option => option.Capability).ToArray();

		return sources.Length switch
		{
			0 => null,
			1 => sources[0],
			_ => new CompositePropertySource(sources),
		};
	}

	private sealed class CompositePropertySource : IPropertySource
	{
		private readonly IReadOnlyList<IPropertySource> _sources;

		public CompositePropertySource(IReadOnlyList<IPropertySource> sources)
		{
			_sources = sources;
		}

		public async ValueTask<IReadOnlyDictionary<string, object?>> GetPropertiesAsync(PropertyRequest request, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);

			var merged = new Dictionary<string, object?>(StringComparer.Ordinal);

			foreach (var source in _sources)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var properties = await source.GetPropertiesAsync(request, cancellationToken).ConfigureAwait(false);

				foreach (var property in properties)
				{
					merged.TryAdd(property.Key, property.Value);
				}
			}

			return new ReadOnlyDictionary<string, object?>(merged);
		}
	}
}
