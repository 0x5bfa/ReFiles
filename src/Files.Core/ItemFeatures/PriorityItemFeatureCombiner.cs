// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Selects the single highest-priority item feature option.
/// </summary>
public sealed class PriorityItemFeatureCombiner<TFeature> : IItemFeatureCombiner<TFeature>
	where TFeature : class
{
	public TFeature? Combine(
		ItemContext context,
		IReadOnlyList<ItemFeatureOption<TFeature>> options)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(options);

		if (options.Count is 0)
		{
			return null;
		}

		var highestPriority = options.Max(static option => option.Priority);
		var matches = options
			.Where(option => option.Priority == highestPriority)
			.ToArray();

		if (matches.Length is not 1)
		{
			throw new InvalidOperationException(
				$"Item feature '{typeof(TFeature).FullName}' has more than one option at priority {highestPriority}.");
		}

		return matches[0].Feature;
	}
}
