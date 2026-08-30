// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities.Thumbnails;

namespace Files.ViewModels;

public sealed class NavigationToolbarBreadcrumbItem
{
	public string Text { get; }

	public BrowseLocation Location { get; }

	public bool IsChevronVisible { get; }

	public ThumbnailResult? Thumbnail { get; }

	public NavigationToolbarBreadcrumbItem(string text, BrowseLocation location, bool isChevronVisible, ThumbnailResult? thumbnail = null)
	{
		ArgumentNullException.ThrowIfNull(text);

		ArgumentNullException.ThrowIfNull(location);

		Text = text;
		Location = location;
		IsChevronVisible = isChevronVisible;
		Thumbnail = thumbnail;
	}
}
