// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;

namespace Files.Core.ItemFeatures;

/// <summary>
/// Creates the optional features attached to an item.
/// </summary>
public sealed class ItemFeatureRegistry
{
	private readonly IReadOnlyDictionary<Type, IReadOnlyList<object>> _factories;
	private readonly ConcurrentDictionary<Type, object> _typedFactories = [];

	private readonly IReadOnlyDictionary<Type, object> _combiners;

	private readonly IReadOnlyDictionary<Type, IReadOnlyList<object>> _wrappers;
	private readonly ConcurrentDictionary<Type, object> _typedWrappers = [];

	/// <summary>Gets an empty feature registry.</summary>
	public static ItemFeatureRegistry Empty { get; } = new ItemFeatureBuilder().Build();

	internal ItemFeatureRegistry(IReadOnlyDictionary<Type, IReadOnlyList<object>> factories, IReadOnlyDictionary<Type, object> combiners, IReadOnlyDictionary<Type, IReadOnlyList<object>> wrappers)
	{
		_factories = factories;
		_combiners = combiners;
		_wrappers = wrappers;
	}

	/// <summary>Creates the feature collection for an item context.</summary>
	/// <param name="context">The item context.</param>
	/// <returns>The feature collection.</returns>
	public IItemFeatures CreateFeatures(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return new ItemFeatures(this, context);
	}

	internal ItemFeatureResolution<TFeature> Resolve<TFeature>(ItemContext context)
		where TFeature : class
	{
		var featureType = typeof(TFeature);
		if (context.CoreModel is not TFeature && !_factories.ContainsKey(featureType) && !_combiners.ContainsKey(featureType) && !_wrappers.ContainsKey(featureType))
		{
			return ItemFeatureResolution<TFeature>.Empty;
		}

		var options = new List<ItemFeatureOption<TFeature>>();
		var ownedInstances = new List<object>();

		try
		{
			if (context.CoreModel is TFeature directFeature)
			{
				options.Add(new ItemFeatureOption<TFeature>(directFeature, 0, "CoreModel", ItemFeatureLifetime.Shared));
			}

			foreach (var registration in GetFactories<TFeature>())
			{
				var feature = registration.Factory.Create(context);

				if (feature is null)
				{
					continue;
				}

				options.Add(new ItemFeatureOption<TFeature>(feature, registration.Priority, registration.Origin, registration.Lifetime));

				if (registration.Lifetime is ItemFeatureLifetime.Item)
				{
					TrackOwned(context, feature, ownedInstances);
				}
			}

			var featureResult = Combine(context, options);

			if (featureResult is null)
			{
				DisposeTrackedInstances(ownedInstances);

				return ItemFeatureResolution<TFeature>.Empty;
			}

			if (!options.Any(option => ReferenceEquals(option.Feature, featureResult)))
			{
				TrackOwned(context, featureResult, ownedInstances);
			}

			foreach (var wrapper in GetWrappers<TFeature>())
			{
				var innerFeature = featureResult;
				featureResult = wrapper.Wrap(context, innerFeature)
					?? throw new InvalidOperationException($"A wrapper returned null for item feature '{typeof(TFeature).FullName}'.");

				if (!ReferenceEquals(innerFeature, featureResult))
				{
					TrackOwned(context, featureResult, ownedInstances);
				}
			}

			return new ItemFeatureResolution<TFeature>(featureResult, ownedInstances);
		}
		catch (Exception resolutionError)
		{
			try
			{
				DisposeTrackedInstances(ownedInstances);
			}
			catch (AggregateException cleanupError)
			{
				throw new AggregateException("Item feature resolution and cleanup failed.", [resolutionError, .. cleanupError.InnerExceptions,]);
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("Item feature resolution and cleanup failed.", resolutionError, cleanupError);
			}

			throw;
		}
	}

	private TFeature? Combine<TFeature>(ItemContext context, IReadOnlyList<ItemFeatureOption<TFeature>> options)
		where TFeature : class
	{
		if (_combiners.TryGetValue(typeof(TFeature), out var combiner))
		{
			return ((IItemFeatureCombiner<TFeature>)combiner).Combine(context, options);
		}

		return options.Count switch
		{
			0 => null,
			1 => options[0].Feature,
			_ => throw new InvalidOperationException($"Item feature '{typeof(TFeature).FullName}' has multiple options but no combiner."),
		};
	}

	private IReadOnlyList<ItemFeatureRegistration<TFeature>> GetFactories<TFeature>()
		where TFeature : class
	{
		if (!_factories.TryGetValue(typeof(TFeature), out var registrations))
		{
			return Array.Empty<ItemFeatureRegistration<TFeature>>();
		}

		if (_typedFactories.TryGetValue(typeof(TFeature), out var cached))
		{
			return (ItemFeatureRegistration<TFeature>[])cached;
		}

		var typedRegistrations = registrations.Cast<ItemFeatureRegistration<TFeature>>().ToArray();
		var resolvedRegistrations = _typedFactories.GetOrAdd(typeof(TFeature), typedRegistrations);

		return (ItemFeatureRegistration<TFeature>[])resolvedRegistrations;
	}

	private IReadOnlyList<IItemFeatureWrapper<TFeature>> GetWrappers<TFeature>()
		where TFeature : class
	{
		if (!_wrappers.TryGetValue(typeof(TFeature), out var registrations))
		{
			return Array.Empty<IItemFeatureWrapper<TFeature>>();
		}

		if (_typedWrappers.TryGetValue(typeof(TFeature), out var cached))
		{
			return (IItemFeatureWrapper<TFeature>[])cached;
		}

		var typedRegistrations = registrations.Cast<IItemFeatureWrapper<TFeature>>().ToArray();
		var resolvedRegistrations = _typedWrappers.GetOrAdd(typeof(TFeature), typedRegistrations);

		return (IItemFeatureWrapper<TFeature>[])resolvedRegistrations;
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
		ItemFeatures.DisposeInstancesAsync(instances).GetAwaiter().GetResult();
	}
}

internal sealed record ItemFeatureResolution<TFeature>(TFeature? Feature, IReadOnlyList<object> OwnedInstances)
	where TFeature : class
{
	public static ItemFeatureResolution<TFeature> Empty { get; } = new(null, Array.Empty<object>());
}
