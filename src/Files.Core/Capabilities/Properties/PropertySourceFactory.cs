// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using Files.Core.Capabilities;

namespace Files.Core.Capabilities.Properties;

/// <summary>
/// Binds a shared property reader to one item.
/// </summary>
public sealed class PropertySourceFactory : ICapabilityFactory<IPropertySource>
{
	private readonly IPropertyReader _reader;

	/// <summary>Initializes a property source factory.</summary>
	/// <param name="reader">The shared property reader.</param>
	public PropertySourceFactory(IPropertyReader reader)
	{
		ArgumentNullException.ThrowIfNull(reader);

		_reader = reader;
	}

	/// <summary>Creates a property source bound to the item context.</summary>
	/// <param name="context">The item context.</param>
	/// <returns>The bound source, or <see langword="null"/> when the reader does not apply.</returns>
	public IPropertySource? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return _reader.CanRead(context)
			? new BoundPropertySource(_reader, context)
			: null;
	}

	private sealed class BoundPropertySource : IPropertySource
	{
		private readonly IPropertyReader _reader;
		private readonly ItemContext _context;

		public BoundPropertySource(IPropertyReader reader, ItemContext context)
		{
			_reader = reader;
			_context = context;
		}

		public async ValueTask<IReadOnlyDictionary<string, object?>> GetPropertiesAsync(PropertyRequest request, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);

			var result = await _reader.GetPropertiesAsync(request, [_context], cancellationToken).ConfigureAwait(false);

			return result.TryGetValue(_context.Reference, out var properties)
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
