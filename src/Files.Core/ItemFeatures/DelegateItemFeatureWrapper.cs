// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Wraps an item feature through a composition-root supplied delegate.
/// </summary>
public sealed class DelegateItemFeatureWrapper<TFeature> : IItemFeatureWrapper<TFeature>
	where TFeature : class
{
	private readonly Func<ItemContext, TFeature, TFeature> _wrap;

	/// <summary>Initializes a delegate-backed feature wrapper.</summary>
	/// <param name="wrap">The delegate that wraps features.</param>
	public DelegateItemFeatureWrapper(Func<ItemContext, TFeature, TFeature> wrap)
	{
		ArgumentNullException.ThrowIfNull(wrap);

		_wrap = wrap;
	}

	/// <summary>Wraps a feature through the configured delegate.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="feature">The feature to wrap.</param>
	/// <returns>The wrapped feature.</returns>
	public TFeature Wrap(ItemContext context, TFeature feature)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(feature);

		return _wrap(context, feature)
			?? throw new InvalidOperationException("An item feature wrapper returned null.");
	}
}
