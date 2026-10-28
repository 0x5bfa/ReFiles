// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ViewSettings;

namespace Files.Core.Browsing;

/// <summary>
/// Coordinates best-effort property and thumbnail reads for a browse viewport.
/// </summary>
public interface IBrowsePrefetchCoordinator : IAsyncDisposable
{
	/// <summary>Updates the range and settings used for prefetching.</summary>
	/// <param name="viewport">The visible item range.</param>
	/// <param name="settings">The current browse view settings.</param>
	/// <param name="browseGeneration">The generation of the browse session.</param>
	void UpdateViewport(BrowseViewport viewport, BrowseViewSettings settings, long browseGeneration);
}
