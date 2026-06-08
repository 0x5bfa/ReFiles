// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.ViewSettings;

/// <summary>
/// Persists presentation state by value-based browse location.
/// </summary>
public interface IViewSettingsStore
{
	ValueTask<BrowseViewSettings?> GetAsync(
		BrowseLocation location,
		CancellationToken cancellationToken = default);

	ValueTask SetAsync(
		BrowseLocation location,
		BrowseViewSettings settings,
		CancellationToken cancellationToken = default);
}
