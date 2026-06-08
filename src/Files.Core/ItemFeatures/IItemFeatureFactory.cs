// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Creates an optional feature when it applies to the supplied item.
/// </summary>
public interface IItemFeatureFactory<TFeature>
	where TFeature : class
{
	TFeature? Create(ItemContext context);
}
