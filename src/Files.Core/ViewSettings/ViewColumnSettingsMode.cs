// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>Specifies how a persisted column layer combines with provider columns.</summary>
public enum ViewColumnSettingsMode
{
	/// <summary>Replaces the inherited column layout.</summary>
	Replace,

	/// <summary>Inserts provider-specific columns into the inherited column layout.</summary>
	Insert,
}
