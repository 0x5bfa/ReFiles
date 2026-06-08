// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Describes who owns an item feature created by a factory.
/// </summary>
public enum ItemFeatureLifetime
{
	/// <summary>
	/// The item owns and disposes the feature.
	/// </summary>
	Item,

	/// <summary>
	/// The factory or another composition root owns the shared feature.
	/// </summary>
	Shared,
}
