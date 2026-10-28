// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>Provides presentation capabilities for a browse location.</summary>
public interface IViewSettingsCapabilityProvider
{
	/// <summary>Gets the presentation capabilities for the current location.</summary>
	/// <param name="cancellationToken">The token used to cancel capability discovery.</param>
	/// <returns>The capabilities, or <see langword="null"/> when unavailable.</returns>
	ValueTask<BrowseViewCapabilities?> GetViewSettingsCapabilitiesAsync(CancellationToken cancellationToken = default);
}
