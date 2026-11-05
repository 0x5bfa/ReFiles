// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>Registers provider contributions and borrowed views without depending on the runtime implementation.</summary>
public interface ICapabilityBuilder
{
	/// <summary>Registers a capability factory.</summary>
	/// <typeparam name="TCapability">The capability interface.</typeparam>
	/// <param name="factory">The contribution factory.</param>
	/// <param name="priority">The contribution priority.</param>
	/// <param name="lifetime">The implementation ownership.</param>
	/// <param name="origin">The diagnostic registration origin.</param>
	/// <returns>This registration surface.</returns>
	ICapabilityBuilder Add<TCapability>(ICapabilityFactory<TCapability> factory, int priority = 0, CapabilityLifetime lifetime = CapabilityLifetime.Item, string? origin = null)
		where TCapability : class, ICapability;

	/// <summary>Registers a capability combiner.</summary>
	/// <typeparam name="TCapability">The capability interface.</typeparam>
	/// <param name="combiner">The contribution combiner.</param>
	/// <returns>This registration surface.</returns>
	ICapabilityBuilder SetCombiner<TCapability>(ICapabilityCombiner<TCapability> combiner) where TCapability : class, ICapability;

	/// <summary>Registers a capability wrapper.</summary>
	/// <typeparam name="TCapability">The capability interface.</typeparam>
	/// <param name="wrapper">The implementation wrapper.</param>
	/// <returns>This registration surface.</returns>
	ICapabilityBuilder AddWrapper<TCapability>(ICapabilityWrapper<TCapability> wrapper) where TCapability : class, ICapability;

	/// <summary>Sets the borrowed view applied after composition.</summary>
	/// <typeparam name="TCapability">The capability interface.</typeparam>
	/// <param name="createView">The borrowed view factory.</param>
	/// <returns>This registration surface.</returns>
	ICapabilityBuilder SetBorrowedView<TCapability>(Func<TCapability, TCapability> createView) where TCapability : class, ICapability;
}
