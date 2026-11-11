// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.ViewSettings;

namespace Files.Settings;

internal sealed class DeferredViewSettingsStore : IViewSettingsStore
{
	private IViewSettingsStore? _inner;

	public ValueTask<BrowseViewSettings?> GetAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		return GetInner().GetAsync(location, cancellationToken);
	}

	public ValueTask SetAsync(BrowseLocation location, BrowseViewSettings settings, CancellationToken cancellationToken = default)
	{
		return GetInner().SetAsync(location, settings, cancellationToken);
	}

	internal void SetInner(IViewSettingsStore inner)
	{
		ArgumentNullException.ThrowIfNull(inner);
		if (Interlocked.CompareExchange(ref _inner, inner, null) is not null)
		{
			throw new InvalidOperationException("The persistent view settings store has already been assigned.");
		}
	}

	private IViewSettingsStore GetInner()
	{
		return Volatile.Read(ref _inner) ?? throw new InvalidOperationException("The persistent view settings store has not been initialized.");
	}
}
