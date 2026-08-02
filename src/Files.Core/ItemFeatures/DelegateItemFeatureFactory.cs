// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Creates an item feature through a composition-root supplied delegate.
/// </summary>
public sealed class DelegateItemFeatureFactory<TFeature> : IItemFeatureFactory<TFeature>
	where TFeature : class
{
	private readonly Func<ItemContext, TFeature?> _factory;

	public DelegateItemFeatureFactory(Func<ItemContext, TFeature?> factory)
	{
		ArgumentNullException.ThrowIfNull(factory);

		_factory = factory;
	}

	public TFeature? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return _factory(context);
	}
}
