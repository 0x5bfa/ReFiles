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
		TFeature?> _combine;

	/// <summary>Initializes a delegate-backed feature combiner.</summary>
	/// <param name="combine">The delegate that combines feature options.</param>
	public DelegateItemFeatureCombiner(Func< ItemContext, IReadOnlyList<ItemFeatureOption<TFeature>>, TFeature?> combine)
	{
		ArgumentNullException.ThrowIfNull(combine);

		_combine = combine;
	}

	/// <summary>Combines feature options through the configured delegate.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="options">The feature options to combine.</param>
	/// <returns>The combined feature.</returns>
	public TFeature? Combine(ItemContext context, IReadOnlyList<ItemFeatureOption<TFeature>> options)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(options);

		return _combine(context, options);
	}
}
