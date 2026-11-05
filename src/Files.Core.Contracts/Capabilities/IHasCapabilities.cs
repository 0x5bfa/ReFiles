// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Exposes optional capabilities without adding them to the model's required contract.
/// </summary>
public interface IHasCapabilities
{
	/// <summary>Gets the optional capabilities attached to the item.</summary>
	ICapabilities Capabilities { get; }
}
