// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Combines item capability options through a composition-root supplied delegate.
/// </summary>
public sealed class DelegateCapabilityCombiner<TCapability> : ICapabilityCombiner<TCapability>
	where TCapability : class
{
	private readonly Func<
		ItemContext,
		IReadOnlyList<CapabilityOption<TCapability>>,
		TCapability?> _combine;

	/// <summary>Initializes a delegate-backed capability combiner.</summary>
	/// <param name="combine">The delegate that combines capability options.</param>
	public DelegateCapabilityCombiner(Func< ItemContext, IReadOnlyList<CapabilityOption<TCapability>>, TCapability?> combine)
	{
		ArgumentNullException.ThrowIfNull(combine);

		_combine = combine;
	}

	/// <summary>Combines capability options through the configured delegate.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="options">The capability options to combine.</param>
	/// <returns>The combined capability.</returns>
	public TCapability? Combine(ItemContext context, IReadOnlyList<CapabilityOption<TCapability>> options)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(options);

		return _combine(context, options);
	}
}
