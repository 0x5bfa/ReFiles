// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>Exposes raw and compatibility-resolved view state for a browse session.</summary>
public interface IViewSettingsSession
{
	/// <summary>Gets the raw settings loaded from the settings store.</summary>
	BrowseViewSettings PersistedViewSettings { get; }

	/// <summary>Applies location capabilities without changing persisted settings.</summary>
	/// <param name="capabilities">The capabilities discovered for the current location.</param>
	/// <param name="cancellationToken">The token used to cancel the update.</param>
	/// <returns>A task that represents the update.</returns>
	ValueTask ApplyViewCapabilitiesAsync(BrowseViewCapabilities capabilities, CancellationToken cancellationToken = default);
}
