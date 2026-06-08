// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ViewSettings;

namespace Files.Core.Browsing;

/// <summary>
/// Coordinates best-effort property and thumbnail reads for a browse viewport.
/// </summary>
public interface IBrowsePrefetchCoordinator : IAsyncDisposable
{
	void UpdateViewport(
		BrowseViewport viewport,
		BrowseViewSettings settings,
		long browseGeneration);
}
