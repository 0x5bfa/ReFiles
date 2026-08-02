// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using Files.Core.ItemFeatures;

namespace Files.Core.ItemFeatures.Properties;

/// <summary>
/// Binds a shared property reader to one item.
/// </summary>
public sealed class PropertySourceFactory : IItemFeatureFactory<IPropertySource>
{
	private readonly IPropertyReader reader;

	public PropertySourceFactory(IPropertyReader reader)
	{
		ArgumentNullException.ThrowIfNull(reader);
		this.reader = reader;
	}

	public IPropertySource? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return reader.CanRead(context)
			? new BoundPropertySource(reader, context)
			: null;
	}

	private sealed class BoundPropertySource : IPropertySource
	{
		private readonly IPropertyReader reader;
		private readonly ItemContext context;

		public BoundPropertySource(IPropertyReader reader, ItemContext context)
		{
			this.reader = reader;
			this.context = context;
		}

		public async ValueTask<IReadOnlyDictionary<string, object?>> GetPropertiesAsync(
			PropertyRequest request,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);

			var result = await reader
				.GetPropertiesAsync(request, [context], cancellationToken)
				.ConfigureAwait(false);

			return result.TryGetValue(context.Reference, out var properties)
				? properties
				: EmptyProperties.Instance;
		}
	}

	private static class EmptyProperties
	{
		public static IReadOnlyDictionary<string, object?> Instance { get; }
			= new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
	}
}
