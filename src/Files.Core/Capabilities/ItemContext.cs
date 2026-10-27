// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Capabilities;

/// <summary>
/// Describes the storage item receiving optional capabilities.
/// </summary>
public sealed record ItemContext(IStorageSource Source, IStorable CoreModel, StorableReference Reference)
{
	internal ICapabilities? Capabilities { get; init; }

	/// <summary>Gets a borrowed capability from this item's composition, or directly from an unbound storage item.</summary>
	/// <typeparam name="TCapability">The optional capability type.</typeparam>
	/// <returns>The borrowed capability, or <see langword="null"/> when unavailable. The caller must not dispose it.</returns>
	/// <exception cref="InvalidOperationException">Composed capabilities form a circular dependency.</exception>
	public TCapability? GetCapability<TCapability>() where TCapability : class
	{
		return Capabilities is null ? CoreModel as TCapability : Capabilities.Get<TCapability>();
	}
}
