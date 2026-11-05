// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Models;

namespace Files.Core.Capabilities;

/// <summary>
/// Describes the storage item receiving optional capabilities.
/// </summary>
public sealed record ItemContext(IStorageSource Source, IStorable CoreModel, StorableReference Reference)
{
	internal ICapabilities? Capabilities { get; init; }
	internal IStorableModelFactory? ModelFactory { get; init; }

	/// <summary>Gets a borrowed capability from this item's composition, or null when the context is unbound.</summary>
	/// <typeparam name="TCapability">The capability interface derived from <see cref="ICapability"/>.</typeparam>
	/// <returns>The borrowed capability, or <see langword="null"/> when unavailable. The caller must not dispose it.</returns>
	/// <exception cref="ArgumentException">The requested type is not a derived capability interface.</exception>
	/// <exception cref="InvalidOperationException">Composed capabilities form a circular dependency.</exception>
	public TCapability? GetCapability<TCapability>() where TCapability : class, ICapability
	{
		CapabilityContract<TCapability>.Validate();

		return Capabilities?.Get<TCapability>();
	}
}
