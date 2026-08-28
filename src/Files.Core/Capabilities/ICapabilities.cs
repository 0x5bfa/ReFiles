// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Files.Core.Capabilities;

/// <summary>
/// Lazily resolves and owns the optional capabilities attached to one item model.
/// </summary>
public interface ICapabilities : IDisposable, IAsyncDisposable
{
	/// <summary>Gets an optional capability of the requested type.</summary>
	/// <typeparam name="TCapability">The capability type.</typeparam>
	/// <returns>The capability, or <see langword="null"/> when it is unavailable.</returns>
	TCapability? Get<TCapability>()
		where TCapability : class;

	/// <summary>Attempts to get an optional capability of the requested type.</summary>
	/// <typeparam name="TCapability">The capability type.</typeparam>
	/// <param name="capability">Receives the capability when one is available.</param>
	/// <returns><see langword="true"/> when a capability was found.</returns>
	bool TryGet<TCapability>([NotNullWhen(true)] out TCapability? capability)
		where TCapability : class;
}
