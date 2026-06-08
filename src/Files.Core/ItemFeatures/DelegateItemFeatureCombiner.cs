// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures;

/// <summary>
/// Combines item feature options through a composition-root supplied delegate.
/// </summary>
public sealed class DelegateItemFeatureCombiner<TFeature> : IItemFeatureCombiner<TFeature>
	where TFeature : class
{
	private readonly Func<
		ItemContext,
		IReadOnlyList<ItemFeatureOption<TFeature>>,
		TFeature?> combine;

	public DelegateItemFeatureCombiner(
		Func<
			ItemContext,
			IReadOnlyList<ItemFeatureOption<TFeature>>,
			TFeature?> combine)
	{
		ArgumentNullException.ThrowIfNull(combine);
		this.combine = combine;
	}

	public TFeature? Combine(
		ItemContext context,
		IReadOnlyList<ItemFeatureOption<TFeature>> options)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(options);
		return combine(context, options);
	}
}
