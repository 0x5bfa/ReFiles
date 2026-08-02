// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;

namespace Files.Commands;

public sealed record CommandContext(RootViewModel Root, object? Parameter = null)
{
	public TabViewModel? ActiveTab => Root.ActiveTab;

	public FolderBrowserViewModel? ActiveFolderBrowser =>
		Root.ActiveFolderBrowser;

	public string? Path => Parameter as string;

	public BrowseItemViewModel? InvokedItem =>
		Parameter as BrowseItemViewModel;

	public TabViewModel? InvokedTab =>
		Parameter as TabViewModel;
}
