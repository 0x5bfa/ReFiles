// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Files.Core.Capabilities;

/// <summary>
/// Discovers borrowed capabilities without granting ownership of their collection.
/// </summary>
public interface ICapabilities
{
	/// <summary>Gets an optional capability of the requested type.</summary>
	/// <typeparam name="TCapability">The capability interface derived from <see cref="ICapability"/>.</typeparam>
	/// <returns>A borrowed capability, or <see langword="null"/> when unavailable. The caller must not dispose the capability.</returns>
	/// <exception cref="ArgumentException">The requested type is not a derived capability interface.</exception>
	TCapability? Get<TCapability>() where TCapability : class, ICapability;

	/// <summary>Attempts to get an optional capability of the requested type.</summary>
	/// <typeparam name="TCapability">The capability interface derived from <see cref="ICapability"/>.</typeparam>
	/// <param name="capability">Receives a borrowed capability when available. The caller must not dispose it.</param>
	/// <returns><see langword="true"/> when a capability was found.</returns>
	/// <exception cref="ArgumentException">The requested type is not a derived capability interface.</exception>
	bool TryGet<TCapability>([NotNullWhen(true)] out TCapability? capability) where TCapability : class, ICapability;
}
