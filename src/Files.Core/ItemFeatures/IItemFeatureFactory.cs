// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Creates an optional feature when it applies to the supplied item.
/// </summary>
public interface IItemFeatureFactory<TFeature>
	where TFeature : class
{
	/// <summary>Creates a feature for an item context.</summary>
	/// <param name="context">The item context.</param>
	/// <returns>The created feature, or <see langword="null"/> when the feature does not apply.</returns>
	TFeature? Create(ItemContext context);
}
