// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.ViewSettings;

/// <summary>Persists partial presentation settings by stable view scope.</summary>
public interface IViewSettingsStore
{
	/// <summary>Gets the settings override stored for a view scope.</summary>
	/// <param name="scope">The stable view scope.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The stored override, or <see langword="null"/> when none exists.</returns>
	ValueTask<BrowseViewSettingsOverride?> GetAsync(ViewSettingsScopeKey scope, CancellationToken cancellationToken = default);

	/// <summary>Stores a settings override for a view scope.</summary>
	/// <param name="scope">The stable view scope.</param>
	/// <param name="settingsOverride">The settings override to store.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	ValueTask SetAsync(ViewSettingsScopeKey scope, BrowseViewSettingsOverride settingsOverride, CancellationToken cancellationToken = default);

	/// <summary>Removes the settings override stored for a view scope.</summary>
	/// <param name="scope">The stable view scope.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns><see langword="true"/> when an override was removed.</returns>
	ValueTask<bool> RemoveAsync(ViewSettingsScopeKey scope, CancellationToken cancellationToken = default);

	/// <summary>Gets complete settings stored for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The stored settings applied to defaults, or <see langword="null"/> when none exists.</returns>
	async ValueTask<BrowseViewSettings?> GetAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		var settingsOverride = await GetAsync(ViewSettingsScopeKey.ForLocation(location), cancellationToken).ConfigureAwait(false);
		if (settingsOverride is null)
		{
			return null;
		}

		return settingsOverride.Fields == ViewSettingsOverrideFields.All ? settingsOverride.Values : settingsOverride.ApplyTo(BrowseViewSettings.Default);
	}

	/// <summary>Stores complete settings for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="settings">The complete settings to store.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	ValueTask SetAsync(BrowseLocation location, BrowseViewSettings settings, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		ArgumentNullException.ThrowIfNull(settings);

		return SetAsync(ViewSettingsScopeKey.ForLocation(location), BrowseViewSettingsOverride.FromSettings(settings), cancellationToken);
	}
}
