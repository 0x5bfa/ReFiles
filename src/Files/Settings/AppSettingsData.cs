// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;

namespace Files.Settings;

internal sealed partial class AppSettingsData
{
	public string? LanguageTag { get; set; }

	public bool ShowFileExtensions { get; set; }

	public bool ShowHiddenItems { get; set; }

	public AppThemeMode ThemeMode { get; set; }

	public double PreviewPaneWidth { get; set; }

	public bool IsPreviewPaneVisible { get; set; }

	public SidebarDisplayMode SidebarDisplayMode { get; set; }
}
