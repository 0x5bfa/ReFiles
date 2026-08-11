// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Exposes optional features without adding them to the model's required contract.
/// </summary>
public interface IHasItemFeatures
{
	/// <summary>Gets the optional features attached to the item.</summary>
	IItemFeatures Features { get; }
}
