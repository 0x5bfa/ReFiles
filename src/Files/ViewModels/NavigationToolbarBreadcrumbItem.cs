// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Storage;

namespace Files.ViewModels;

public sealed class NavigationToolbarBreadcrumbItem
{
	public string Text { get; }

	public BrowseLocation Location { get; }

	public bool IsChevronVisible { get; }

	public ThumbnailResult? Thumbnail { get; }

	public StorableReference? ShellReference { get; }

	public bool SupportsShellDragDrop => ShellReference is not null;

	public NavigationToolbarBreadcrumbItem(string text, BrowseLocation location, bool isChevronVisible, ThumbnailResult? thumbnail = null, bool supportsShellDragDrop = false)
	{
		ArgumentNullException.ThrowIfNull(text);

		ArgumentNullException.ThrowIfNull(location);

		Text = text;
		Location = location;
		IsChevronVisible = isChevronVisible;
		Thumbnail = thumbnail;
		ShellReference = supportsShellDragDrop && location is FolderLocation folder ? folder.Folder : null;
	}
}
