// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Files.Core.ItemFeatures;

/// <summary>
/// Provides concise access to optional features exposed by a model.
/// </summary>
public static class ItemFeatureExtensions
{
	/// <summary>Gets an optional feature from an item.</summary>
	/// <typeparam name="TFeature">The feature type.</typeparam>
	/// <param name="host">The item exposing features.</param>
	/// <returns>The feature, or <see langword="null"/> when it is unavailable.</returns>
	public static TFeature? Get<TFeature>(this IHasItemFeatures host)
		where TFeature : class
	{
		ArgumentNullException.ThrowIfNull(host);

		return host.Features.Get<TFeature>();
	}

	/// <summary>Attempts to get an optional feature from an item.</summary>
	/// <typeparam name="TFeature">The feature type.</typeparam>
	/// <param name="host">The item exposing features.</param>
	/// <param name="feature">Receives the feature when one is available.</param>
	/// <returns><see langword="true"/> when a feature was found.</returns>
	public static bool TryGet<TFeature>(this IHasItemFeatures host, [NotNullWhen(true)] out TFeature? feature)
		where TFeature : class
	{
		ArgumentNullException.ThrowIfNull(host);

		return host.Features.TryGet(out feature);
	}
}
