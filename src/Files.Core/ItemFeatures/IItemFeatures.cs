// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Files.Core.ItemFeatures;

/// <summary>
/// Lazily resolves and owns the optional features attached to one item model.
/// </summary>
public interface IItemFeatures : IDisposable, IAsyncDisposable
{
	/// <summary>Gets an optional feature of the requested type.</summary>
	/// <typeparam name="TFeature">The feature type.</typeparam>
	/// <returns>The feature, or <see langword="null"/> when it is unavailable.</returns>
	TFeature? Get<TFeature>()
		where TFeature : class;

	/// <summary>Attempts to get an optional feature of the requested type.</summary>
	/// <typeparam name="TFeature">The feature type.</typeparam>
	/// <param name="feature">Receives the feature when one is available.</param>
	/// <returns><see langword="true"/> when a feature was found.</returns>
	bool TryGet<TFeature>([NotNullWhen(true)] out TFeature? feature)
		where TFeature : class;
}
