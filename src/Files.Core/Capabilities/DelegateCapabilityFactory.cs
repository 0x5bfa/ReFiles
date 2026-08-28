// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Creates an item capability through a composition-root supplied delegate.
/// </summary>
public sealed class DelegateCapabilityFactory<TCapability> : ICapabilityFactory<TCapability>
	where TCapability : class
{
	private readonly Func<ItemContext, TCapability?> _factory;

	/// <summary>Initializes a delegate-backed capability factory.</summary>
	/// <param name="factory">The delegate that creates capabilities.</param>
	public DelegateCapabilityFactory(Func<ItemContext, TCapability?> factory)
	{
		ArgumentNullException.ThrowIfNull(factory);

		_factory = factory;
	}

	/// <summary>Creates a capability through the configured delegate.</summary>
	/// <param name="context">The item context.</param>
	/// <returns>The created capability, or <see langword="null"/> when it does not apply.</returns>
	public TCapability? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return _factory(context);
	}
}
