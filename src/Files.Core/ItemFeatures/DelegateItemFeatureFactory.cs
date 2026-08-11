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

	/// <summary>Initializes a delegate-backed feature factory.</summary>
	/// <param name="factory">The delegate that creates features.</param>
	public DelegateItemFeatureFactory(Func<ItemContext, TFeature?> factory)
	{
		ArgumentNullException.ThrowIfNull(factory);

		_factory = factory;
	}

	/// <summary>Creates a feature through the configured delegate.</summary>
	/// <param name="context">The item context.</param>
	/// <returns>The created feature, or <see langword="null"/> when it does not apply.</returns>
	public TFeature? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return _factory(context);
	}
}
