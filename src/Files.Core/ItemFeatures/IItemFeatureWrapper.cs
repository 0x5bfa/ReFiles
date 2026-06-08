// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Wraps an item feature with cross-cutting behavior.
/// </summary>
public interface IItemFeatureWrapper<TFeature>
	where TFeature : class
{
	TFeature Wrap(ItemContext context, TFeature feature);
}
