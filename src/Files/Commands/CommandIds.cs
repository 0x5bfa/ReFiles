// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Commands;

public static class CommandIds
{
	public static readonly CommandId NavigateBack =
		new("files.navigation.back");

	public static readonly CommandId NavigateForward =
		new("files.navigation.forward");

	public static readonly CommandId NavigateUp =
		new("files.navigation.up");

	public static readonly CommandId NavigateHome =
		new("files.navigation.home");

	public static readonly CommandId NavigatePath =
		new("files.navigation.path");

	public static readonly CommandId Refresh =
		new("files.navigation.refresh");

	public static readonly CommandId OpenItem =
		new("files.item.open");

	public static readonly CommandId NewTab =
		new("files.tab.new");

	public static readonly CommandId CloseTab =
		new("files.tab.close");

	public static readonly CommandId NewPane =
		new("files.pane.new");

	public static readonly CommandId ClosePane =
		new("files.pane.close");

	public static readonly CommandId LayoutDetails =
		new("files.layout.details");

	public static readonly CommandId LayoutList =
		new("files.layout.list");

	public static readonly CommandId LayoutGrid =
		new("files.layout.grid");

	public static readonly CommandId SortItems =
		new("files.display.sort");

	public static readonly CommandId GroupItems =
		new("files.display.group");
}
