// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Combines multiple options for one item feature.
/// </summary>
public interface IItemFeatureCombiner<TFeature>
	where TFeature : class
{
	TFeature? Combine(ItemContext context, IReadOnlyList<ItemFeatureOption<TFeature>> options);
}
