// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Registers factories, combiners, and wrappers for optional item features.
/// </summary>
public sealed class ItemFeatureBuilder
{
	private readonly Dictionary<Type, List<object>> factories = [];
	private readonly Dictionary<Type, object> combiners = [];
	private readonly Dictionary<Type, List<object>> wrappers = [];

	public ItemFeatureBuilder Add<TFeature>(
		IItemFeatureFactory<TFeature> factory,
		int priority = 0,
		ItemFeatureLifetime lifetime = ItemFeatureLifetime.Item,
		string? origin = null)
		where TFeature : class
	{
		ArgumentNullException.ThrowIfNull(factory);

		var registration = new ItemFeatureRegistration<TFeature>(
			factory,
			priority,
			lifetime,
			origin ?? factory.GetType().Name);

		GetOrCreateList(factories, typeof(TFeature)).Add(registration);
		return this;
	}

	public ItemFeatureBuilder SetCombiner<TFeature>(
		IItemFeatureCombiner<TFeature> combiner)
		where TFeature : class
	{
		ArgumentNullException.ThrowIfNull(combiner);

		if (!combiners.TryAdd(typeof(TFeature), combiner))
		{
			throw new InvalidOperationException(
				$"A combiner is already registered for item feature '{typeof(TFeature).FullName}'.");
		}

		return this;
	}

	public ItemFeatureBuilder AddWrapper<TFeature>(
		IItemFeatureWrapper<TFeature> wrapper)
		where TFeature : class
	{
		ArgumentNullException.ThrowIfNull(wrapper);
		GetOrCreateList(wrappers, typeof(TFeature)).Add(wrapper);
		return this;
	}

	public ItemFeatureRegistry Build()
	{
		return new ItemFeatureRegistry(
			CloneLists(factories),
			new Dictionary<Type, object>(combiners),
			CloneLists(wrappers));
	}

	private static List<object> GetOrCreateList(
		Dictionary<Type, List<object>> registrations,
		Type featureType)
	{
		if (!registrations.TryGetValue(featureType, out var values))
		{
			values = [];
			registrations.Add(featureType, values);
		}

		return values;
	}

	private static Dictionary<Type, IReadOnlyList<object>> CloneLists(
		Dictionary<Type, List<object>> registrations)
	{
		return registrations.ToDictionary(
			static pair => pair.Key,
			static pair => (IReadOnlyList<object>)pair.Value.ToArray());
	}
}

internal sealed record ItemFeatureRegistration<TFeature>(
	IItemFeatureFactory<TFeature> Factory,
	int Priority,
	ItemFeatureLifetime Lifetime,
	string Origin)
	where TFeature : class;
