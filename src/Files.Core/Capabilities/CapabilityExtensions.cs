// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Files.Core.Capabilities;

/// <summary>
/// Provides concise access to optional capabilities exposed by a model.
/// </summary>
public static class CapabilityExtensions
{
	/// <summary>Gets an optional capability from an item.</summary>
	/// <typeparam name="TCapability">The capability type.</typeparam>
	/// <param name="host">The item exposing capabilities.</param>
	/// <returns>The capability, or <see langword="null"/> when it is unavailable.</returns>
	public static TCapability? Get<TCapability>(this IHasCapabilities host)
		where TCapability : class
	{
		ArgumentNullException.ThrowIfNull(host);

		return host.Capabilities.Get<TCapability>();
	}

	/// <summary>Attempts to get an optional capability from an item.</summary>
	/// <typeparam name="TCapability">The capability type.</typeparam>
	/// <param name="host">The item exposing capabilities.</param>
	/// <param name="capability">Receives the capability when one is available.</param>
	/// <returns><see langword="true"/> when a capability was found.</returns>
	public static bool TryGet<TCapability>(this IHasCapabilities host, [NotNullWhen(true)] out TCapability? capability)
		where TCapability : class
	{
		ArgumentNullException.ThrowIfNull(host);

		return host.Capabilities.TryGet(out capability);
	}
}
