// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Wraps an item capability through a composition-root supplied delegate.
/// </summary>
public sealed class DelegateCapabilityWrapper<TCapability> : ICapabilityWrapper<TCapability>
	where TCapability : class
{
	private readonly Func<ItemContext, TCapability, TCapability> _wrap;

	/// <summary>Initializes a delegate-backed capability wrapper.</summary>
	/// <param name="wrap">The delegate that wraps capabilities.</param>
	public DelegateCapabilityWrapper(Func<ItemContext, TCapability, TCapability> wrap)
	{
		ArgumentNullException.ThrowIfNull(wrap);

		_wrap = wrap;
	}

	/// <summary>Wraps a capability through the configured delegate.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="capability">The capability to wrap.</param>
	/// <returns>The wrapped capability.</returns>
	public TCapability Wrap(ItemContext context, TCapability capability)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(capability);

		return _wrap(context, capability)
			?? throw new InvalidOperationException("An item capability wrapper returned null.");
	}
}
