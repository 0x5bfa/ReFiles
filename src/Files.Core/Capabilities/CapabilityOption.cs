// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Describes one available implementation of an item capability.
/// </summary>
public sealed record CapabilityOption<TCapability>(TCapability Capability, int Priority, string Origin, CapabilityLifetime Lifetime)
	where TCapability : class;
