// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.ViewSettings;

/// <summary>
/// Persists presentation state by value-based browse location.
/// </summary>
public interface IViewSettingsStore
{
	/// <summary>Gets the settings stored for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The stored settings, or <see langword="null"/> when none exist.</returns>
	ValueTask<BrowseViewSettings?> GetAsync(BrowseLocation location, CancellationToken cancellationToken = default);

	/// <summary>Stores settings for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="settings">The settings to store.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	ValueTask SetAsync(BrowseLocation location, BrowseViewSettings settings, CancellationToken cancellationToken = default);
}
