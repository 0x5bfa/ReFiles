// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Wraps an item capability with cross-cutting behavior.
/// </summary>
/// <typeparam name="TCapability">The capability interface derived from <see cref="ICapability"/>.</typeparam>
public interface ICapabilityWrapper<TCapability>
	where TCapability : class, ICapability
{
	/// <summary>Wraps a capability for an item context.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="capability">The capability to wrap.</param>
	/// <returns>The wrapped capability.</returns>
	TCapability Wrap(ItemContext context, TCapability capability);
}
