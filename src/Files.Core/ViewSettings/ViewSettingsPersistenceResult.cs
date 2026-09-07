// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>Describes a provider persistence result and its application-owned remainder.</summary>
public sealed record ViewSettingsPersistenceResult
{
	/// <summary>Gets the complete provider-owned state after the write, or <see langword="null"/> when the provider state was unavailable. An empty override represents no stored settings.</summary>
	public BrowseViewSettingsOverride? ProviderSettings { get; }

	/// <summary>Gets requested settings that must remain in application persistence.</summary>
	public BrowseViewSettingsOverride ApplicationSettings { get; }

	/// <summary>Initializes a provider persistence result.</summary>
	/// <param name="providerSettings">The complete provider-owned state after the write, or <see langword="null"/> when unavailable. An empty override represents no stored settings.</param>
	/// <param name="applicationSettings">Requested settings that require application persistence.</param>
	public ViewSettingsPersistenceResult(BrowseViewSettingsOverride? providerSettings, BrowseViewSettingsOverride applicationSettings)
	{
		ArgumentNullException.ThrowIfNull(applicationSettings);

		ProviderSettings = providerSettings;
		ApplicationSettings = applicationSettings;
	}
}
