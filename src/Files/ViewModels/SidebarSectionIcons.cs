// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.UI.ViewManagement;

namespace Files.ViewModels;

internal enum SidebarSectionType
{
	Home,
	Pinned,
	Library,
	Drives,
	CloudDrives,
	Network,
	WSL,
	FileTag,
}

internal static class SidebarSectionIcons
{
	private const string BasePath = "ms-appx:///Assets/FluentIcons/SidebarSections/";

	private static readonly AccessibilitySettings _accessibilitySettings = new();

	private static readonly UISettings _uiSettings = new();

	public static string For(SidebarSectionType section)
	{
		var name = section switch
		{
			SidebarSectionType.Home => "Home",
			SidebarSectionType.Pinned => "Pinned",
			SidebarSectionType.Library => "Libraries",
			SidebarSectionType.Drives => "Drives",
			SidebarSectionType.CloudDrives => "CloudDrives",
			SidebarSectionType.Network => "Network",
			SidebarSectionType.WSL => "Wsl",
			SidebarSectionType.FileTag => "Tags",
			_ => throw new ArgumentOutOfRangeException(nameof(section), section, "The sidebar section type is not supported."),
		};

		return Resolve(name);
	}

	private static string Resolve(string name)
	{
		if (!_accessibilitySettings.HighContrast)
		{
			return $"{BasePath}{name}.png";
		}

		var background = _uiSettings.GetColorValue(UIColorType.Background);
		var suffix = background.R + background.G + background.B < 384 ? "Black" : "White";

		return $"{BasePath}{name}-{suffix}.png";
	}
}
