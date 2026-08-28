// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;

namespace Files.Core.Capabilities;

/// <summary>
/// Creates the optional capabilities attached to an item.
/// </summary>
public sealed class CapabilityRegistry
{
	private readonly IReadOnlyDictionary<Type, IReadOnlyList<object>> _factories;
	private readonly ConcurrentDictionary<Type, object> _typedFactories = [];

	private readonly IReadOnlyDictionary<Type, object> _combiners;

	private readonly IReadOnlyDictionary<Type, IReadOnlyList<object>> _wrappers;
	private readonly ConcurrentDictionary<Type, object> _typedWrappers = [];

	/// <summary>Gets an empty capability registry.</summary>
	public static CapabilityRegistry Empty { get; } = new CapabilityBuilder().Build();

	internal CapabilityRegistry(IReadOnlyDictionary<Type, IReadOnlyList<object>> factories, IReadOnlyDictionary<Type, object> combiners, IReadOnlyDictionary<Type, IReadOnlyList<object>> wrappers)
	{
		_factories = factories;
		_combiners = combiners;
		_wrappers = wrappers;
	}

	/// <summary>Creates the capability collection for an item context.</summary>
	/// <param name="context">The item context.</param>
	/// <returns>The capability collection.</returns>
	public ICapabilities CreateCapabilities(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return new Capabilities(this, context);
	}

	internal CapabilityResolution<TCapability> Resolve<TCapability>(ItemContext context)
		where TCapability : class
	{
		var capabilityType = typeof(TCapability);
		if (context.CoreModel is not TCapability && !_factories.ContainsKey(capabilityType) && !_combiners.ContainsKey(capabilityType) && !_wrappers.ContainsKey(capabilityType))
		{
			return CapabilityResolution<TCapability>.Empty;
		}

		var options = new List<CapabilityOption<TCapability>>();
		var ownedInstances = new List<object>();

		try
		{
			if (context.CoreModel is TCapability directCapability)
			{
				options.Add(new CapabilityOption<TCapability>(directCapability, 0, "CoreModel", CapabilityLifetime.Shared));
			}

			foreach (var registration in GetFactories<TCapability>())
			{
				var capability = registration.Factory.Create(context);

				if (capability is null)
				{
					continue;
				}

				options.Add(new CapabilityOption<TCapability>(capability, registration.Priority, registration.Origin, registration.Lifetime));

				if (registration.Lifetime is CapabilityLifetime.Item)
				{
					TrackOwned(context, capability, ownedInstances);
				}
			}

			var capabilityResult = Combine(context, options);

			if (capabilityResult is null)
			{
				DisposeTrackedInstances(ownedInstances);

				return CapabilityResolution<TCapability>.Empty;
			}

			if (!options.Any(option => ReferenceEquals(option.Capability, capabilityResult)))
			{
				TrackOwned(context, capabilityResult, ownedInstances);
			}

			foreach (var wrapper in GetWrappers<TCapability>())
			{
				var innerCapability = capabilityResult;
				capabilityResult = wrapper.Wrap(context, innerCapability)
					?? throw new InvalidOperationException($"A wrapper returned null for item capability '{typeof(TCapability).FullName}'.");

				if (!ReferenceEquals(innerCapability, capabilityResult))
				{
					TrackOwned(context, capabilityResult, ownedInstances);
				}
			}

			return new CapabilityResolution<TCapability>(capabilityResult, ownedInstances);
		}
		catch (Exception resolutionError)
		{
			try
			{
				DisposeTrackedInstances(ownedInstances);
			}
			catch (AggregateException cleanupError)
			{
				throw new AggregateException("Item capability resolution and cleanup failed.", [resolutionError, .. cleanupError.InnerExceptions,]);
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("Item capability resolution and cleanup failed.", resolutionError, cleanupError);
			}

			throw;
		}
	}

	private TCapability? Combine<TCapability>(ItemContext context, IReadOnlyList<CapabilityOption<TCapability>> options)
		where TCapability : class
	{
		if (_combiners.TryGetValue(typeof(TCapability), out var combiner))
		{
			return ((ICapabilityCombiner<TCapability>)combiner).Combine(context, options);
		}

		return options.Count switch
		{
			0 => null,
			1 => options[0].Capability,
			_ => throw new InvalidOperationException($"Item capability '{typeof(TCapability).FullName}' has multiple options but no combiner."),
		};
	}

	private IReadOnlyList<CapabilityRegistration<TCapability>> GetFactories<TCapability>()
		where TCapability : class
	{
		if (!_factories.TryGetValue(typeof(TCapability), out var registrations))
		{
			return Array.Empty<CapabilityRegistration<TCapability>>();
		}

		if (_typedFactories.TryGetValue(typeof(TCapability), out var cached))
		{
			return (CapabilityRegistration<TCapability>[])cached;
		}

		var typedRegistrations = registrations.Cast<CapabilityRegistration<TCapability>>().ToArray();
		var resolvedRegistrations = _typedFactories.GetOrAdd(typeof(TCapability), typedRegistrations);

		return (CapabilityRegistration<TCapability>[])resolvedRegistrations;
	}

	private IReadOnlyList<ICapabilityWrapper<TCapability>> GetWrappers<TCapability>()
		where TCapability : class
	{
		if (!_wrappers.TryGetValue(typeof(TCapability), out var registrations))
		{
			return Array.Empty<ICapabilityWrapper<TCapability>>();
		}

		if (_typedWrappers.TryGetValue(typeof(TCapability), out var cached))
		{
			return (ICapabilityWrapper<TCapability>[])cached;
		}

		var typedRegistrations = registrations.Cast<ICapabilityWrapper<TCapability>>().ToArray();
		var resolvedRegistrations = _typedWrappers.GetOrAdd(typeof(TCapability), typedRegistrations);

		return (ICapabilityWrapper<TCapability>[])resolvedRegistrations;
	}

	private static void TrackOwned(ItemContext context, object instance, List<object> ownedInstances)
	{
		if (ReferenceEquals(instance, context.CoreModel) || ReferenceEquals(instance, context.Source) || ownedInstances.Any(existing => ReferenceEquals(existing, instance)))
		{
			return;
		}

		if (instance is IDisposable or IAsyncDisposable)
		{
			ownedInstances.Add(instance);
		}
	}

	private static void DisposeTrackedInstances(List<object> ownedInstances)
	{
		var instances = ownedInstances.ToArray();
		ownedInstances.Clear();
		Capabilities.DisposeInstancesAsync(instances).GetAwaiter().GetResult();
	}
}

internal sealed record CapabilityResolution<TCapability>(TCapability? Capability, IReadOnlyList<object> OwnedInstances)
	where TCapability : class
{
	public static CapabilityResolution<TCapability> Empty { get; } = new(null, Array.Empty<object>());
}
