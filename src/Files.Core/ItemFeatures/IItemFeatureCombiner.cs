// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Combines multiple options for one item feature.
/// </summary>
public interface IItemFeatureCombiner<TFeature>
	where TFeature : class
{
	/// <summary>Combines the available feature options.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="options">The feature options to combine.</param>
	/// <returns>The combined feature, or <see langword="null"/> when no feature applies.</returns>
	TFeature? Combine(ItemContext context, IReadOnlyList<ItemFeatureOption<TFeature>> options);
}
