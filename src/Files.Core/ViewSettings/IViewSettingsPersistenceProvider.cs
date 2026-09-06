// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>Persists view settings in the store owned by a browse provider.</summary>
public interface IViewSettingsPersistenceProvider
{
	/// <summary>Gets view settings persisted by the provider.</summary>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The complete provider layer, an empty override when no settings are stored, or <see langword="null"/> when provider state is unavailable.</returns>
	ValueTask<BrowseViewSettingsOverride?> GetViewSettingsAsync(CancellationToken cancellationToken = default);

	/// <summary>Persists supported settings and returns the application-owned remainder.</summary>
	/// <param name="settingsOverride">The requested settings.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The resulting provider state and settings that require application persistence.</returns>
	ValueTask<ViewSettingsPersistenceResult> SetViewSettingsAsync(BrowseViewSettingsOverride settingsOverride, CancellationToken cancellationToken = default);

	/// <summary>Clears selected settings from provider persistence.</summary>
	/// <param name="fields">The settings to clear.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The complete remaining provider layer, an empty override when no settings remain, or <see langword="null"/> when provider state is unavailable.</returns>
	ValueTask<BrowseViewSettingsOverride?> ClearViewSettingsAsync(ViewSettingsOverrideFields fields, CancellationToken cancellationToken = default);
}
