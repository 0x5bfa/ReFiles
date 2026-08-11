// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Wraps an item feature with cross-cutting behavior.
/// </summary>
public interface IItemFeatureWrapper<TFeature>
	where TFeature : class
{
	/// <summary>Wraps a feature for an item context.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="feature">The feature to wrap.</param>
	/// <returns>The wrapped feature.</returns>
	TFeature Wrap(ItemContext context, TFeature feature);
}
