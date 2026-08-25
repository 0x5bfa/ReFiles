// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.ViewModels;

public sealed class NavigationToolbarBreadcrumbItem
{
	public string Text { get; }

	public BrowseLocation Location { get; }

	public bool IsChevronVisible { get; }

	public ReadOnlyMemory<byte> ThumbnailData { get; }

	public NavigationToolbarBreadcrumbItem(string text, BrowseLocation location, bool isChevronVisible, ReadOnlyMemory<byte> thumbnailData = default)
	{
		ArgumentNullException.ThrowIfNull(text);

		ArgumentNullException.ThrowIfNull(location);

		Text = text;
		Location = location;
		IsChevronVisible = isChevronVisible;
		ThumbnailData = thumbnailData;
	}
}
