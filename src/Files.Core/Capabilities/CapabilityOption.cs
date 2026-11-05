// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Describes one available implementation of an item capability.
/// </summary>
/// <typeparam name="TCapability">The capability interface derived from <see cref="ICapability"/>.</typeparam>
/// <param name="Capability">The contributed capability.</param>
/// <param name="Priority">The priority used by the combiner.</param>
/// <param name="Origin">The diagnostic name of the contributor.</param>
/// <param name="Lifetime">The ownership of the contributed capability.</param>
public sealed record CapabilityOption<TCapability>(TCapability Capability, int Priority, string Origin, CapabilityLifetime Lifetime)
	where TCapability : class, ICapability;
