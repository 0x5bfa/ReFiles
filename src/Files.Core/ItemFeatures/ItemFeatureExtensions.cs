// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Files.Core.ItemFeatures;

/// <summary>
/// Provides concise access to optional features exposed by a model.
/// </summary>
public static class ItemFeatureExtensions
{
	public static TFeature? Get<TFeature>(this IHasItemFeatures host)
		where TFeature : class
	{
		ArgumentNullException.ThrowIfNull(host);

		return host.Features.Get<TFeature>();
	}

	public static bool TryGet<TFeature>(this IHasItemFeatures host, [NotNullWhen(true)] out TFeature? feature)
		where TFeature : class
	{
		ArgumentNullException.ThrowIfNull(host);

		return host.Features.TryGet(out feature);
	}
}
