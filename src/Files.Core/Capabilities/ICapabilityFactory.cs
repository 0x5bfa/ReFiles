// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Creates an optional capability when it applies to the supplied item.
/// </summary>
public interface ICapabilityFactory<TCapability>
	where TCapability : class
{
	/// <summary>Creates a capability for an item context.</summary>
	/// <param name="context">The item context.</param>
	/// <returns>The created capability, or <see langword="null"/> when the capability does not apply.</returns>
	TCapability? Create(ItemContext context);
}
