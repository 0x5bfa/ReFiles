// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Combines multiple options for one item capability.
/// </summary>
public interface ICapabilityCombiner<TCapability>
	where TCapability : class
{
	/// <summary>Combines the available capability options.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="options">The capability options to combine.</param>
	/// <returns>The combined capability, or <see langword="null"/> when no capability applies.</returns>
	TCapability? Combine(ItemContext context, IReadOnlyList<CapabilityOption<TCapability>> options);
}
