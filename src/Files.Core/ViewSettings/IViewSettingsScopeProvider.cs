// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>Provides a stable persistence scope for a custom browse location.</summary>
public interface IViewSettingsScopeProvider
{
	/// <summary>Gets the stable persistence scope for the browse location.</summary>
	ViewSettingsScopeKey ViewSettingsScope { get; }
}
