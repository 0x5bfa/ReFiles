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

	public DelegateItemFeatureWrapper(Func<ItemContext, TFeature, TFeature> wrap)
	{
		ArgumentNullException.ThrowIfNull(wrap);

		_wrap = wrap;
	}

	public TFeature Wrap(ItemContext context, TFeature feature)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(feature);

		return _wrap(context, feature)
			?? throw new InvalidOperationException("An item feature wrapper returned null.");
	}
}
