// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Describes one available implementation of an item feature.
/// </summary>
public sealed record ItemFeatureOption<TFeature>(TFeature Feature, int Priority, string Origin, ItemFeatureLifetime Lifetime)
	where TFeature : class;
