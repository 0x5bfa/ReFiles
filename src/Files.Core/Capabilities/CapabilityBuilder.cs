// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Registers factories, combiners, and wrappers for optional item capabilities.
/// </summary>
public sealed class CapabilityBuilder
{
	private readonly Dictionary<Type, List<object>> _factories = [];
	private readonly Dictionary<Type, object> _combiners = [];
	private readonly Dictionary<Type, List<object>> _wrappers = [];

	/// <summary>Registers a capability factory.</summary>
	/// <typeparam name="TCapability">The capability type.</typeparam>
	/// <param name="factory">The factory that creates the capability.</param>
	/// <param name="priority">The priority used by the capability combiner.</param>
	/// <param name="lifetime">The lifetime of created capability instances.</param>
	/// <param name="origin">The registration origin used for diagnostics.</param>
	/// <returns>This builder.</returns>
	public CapabilityBuilder Add<TCapability>(ICapabilityFactory<TCapability> factory, int priority = 0, CapabilityLifetime lifetime = CapabilityLifetime.Item, string? origin = null)
		where TCapability : class
	{
		ArgumentNullException.ThrowIfNull(factory);

		var registration = new CapabilityRegistration<TCapability>(factory, priority, lifetime, origin ?? factory.GetType().Name);

		GetOrCreateList(_factories, typeof(TCapability)).Add(registration);

		return this;
	}

	/// <summary>Registers the combiner for a capability type.</summary>
	/// <typeparam name="TCapability">The capability type.</typeparam>
	/// <param name="combiner">The combiner to use.</param>
	/// <returns>This builder.</returns>
	public CapabilityBuilder SetCombiner<TCapability>(ICapabilityCombiner<TCapability> combiner)
		where TCapability : class
	{
		ArgumentNullException.ThrowIfNull(combiner);

		if (!_combiners.TryAdd(typeof(TCapability), combiner))
		{
			throw new InvalidOperationException($"A combiner is already registered for item capability '{typeof(TCapability).FullName}'.");
		}

		return this;
	}

	/// <summary>Registers a wrapper for a capability type.</summary>
	/// <typeparam name="TCapability">The capability type.</typeparam>
	/// <param name="wrapper">The wrapper to apply.</param>
	/// <returns>This builder.</returns>
	public CapabilityBuilder AddWrapper<TCapability>(ICapabilityWrapper<TCapability> wrapper)
		where TCapability : class
	{
		ArgumentNullException.ThrowIfNull(wrapper);

		GetOrCreateList(_wrappers, typeof(TCapability)).Add(wrapper);

		return this;
	}

	/// <summary>Builds an immutable capability registry from the registrations.</summary>
	/// <returns>The capability registry.</returns>
	public CapabilityRegistry Build()
	{
		return new CapabilityRegistry(CloneLists(_factories), new Dictionary<Type, object>(_combiners), CloneLists(_wrappers));
	}

	private static List<object> GetOrCreateList(Dictionary<Type, List<object>> registrations, Type capabilityType)
	{
		if (!registrations.TryGetValue(capabilityType, out var values))
		{
			values = [];
			registrations.Add(capabilityType, values);
		}

		return values;
	}

	private static Dictionary<Type, IReadOnlyList<object>> CloneLists(Dictionary<Type, List<object>> registrations)
	{
		return registrations.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyList<object>)pair.Value.ToArray());
	}
}

internal sealed record CapabilityRegistration<TCapability>(ICapabilityFactory<TCapability> Factory, int Priority, CapabilityLifetime Lifetime, string Origin)
	where TCapability : class;
