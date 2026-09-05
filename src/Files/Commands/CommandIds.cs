// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Commands;

public static class CommandIds
{
	public static readonly CommandId ToggleSidebar =
		new("files.navigation.toggle-sidebar");

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

	public static readonly CommandId Search = new("files.navigation.search");

	public static readonly CommandId Refresh =
		new("files.navigation.refresh");

	public static readonly CommandId OpenItem =
		new("files.item.open");

	public static readonly CommandId Copy =
		new("files.item.copy");

	public static readonly CommandId Cut =
		new("files.item.cut");

	public static readonly CommandId Paste =
		new("files.item.paste");

	public static readonly CommandId Delete =
		new("files.item.delete");

	public static readonly CommandId SelectAll =
		new("files.selection.select-all");

	public static readonly CommandId InvertSelection =
		new("files.selection.invert");

	public static readonly CommandId ClearSelection =
		new("files.selection.clear");

	public static readonly CommandId Properties = new("files.item.properties");

	public static readonly CommandId Mount = new("files.shell.mount");

	public static readonly CommandId BurnDiscImage = new("files.shell.burn-disc-image");

	public static readonly CommandId SetDesktopBackground = new("files.shell.set-desktop-background");

	public static readonly CommandId EmptyRecycleBin = new("files.shell.empty-recycle-bin");

	public static readonly CommandId RestoreAllRecycleBinItems = new("files.shell.restore-all-recycle-bin-items");

	public static readonly CommandId RestoreRecycleBinItems = new("files.shell.restore-recycle-bin-items");

	public static readonly CommandId CompressToZip = new("files.shell.compress-to-zip");

	public static readonly CommandId PinToQuickAccess = new("files.shell.pin-to-quick-access");

	public static readonly CommandId AddToFavorites = new("files.shell.add-to-favorites");

	public static readonly CommandId CopyAsPath = new("files.shell.copy-as-path");

	public static readonly CommandId NewTab =
		new("files.tab.new");

	public static readonly CommandId DuplicateTab =
		new("files.tab.duplicate");

	public static readonly CommandId MoveTabToNewWindow =
		new("files.tab.move-to-new-window");

	public static readonly CommandId CloseTabsToLeft =
		new("files.tab.close-to-left");

	public static readonly CommandId CloseTabsToRight =
		new("files.tab.close-to-right");

	public static readonly CommandId CloseOtherTabs =
		new("files.tab.close-other");

	public static readonly CommandId ReopenTab =
		new("files.tab.reopen");

	public static readonly CommandId CloseTab =
		new("files.tab.close");

	public static readonly CommandId NewPane =
		new("files.pane.new");

	public static readonly CommandId ClosePane =
		new("files.pane.close");

	public static readonly CommandId SplitPaneVertical =
		new("files.pane.split-vertical");

	public static readonly CommandId SplitPaneHorizontal =
		new("files.pane.split-horizontal");

	public static readonly CommandId LayoutDetails =
		new("files.layout.details");

	public static readonly CommandId LayoutList =
		new("files.layout.list");

	public static readonly CommandId LayoutCards =
		new("files.layout.cards");

	public static readonly CommandId LayoutGrid =
		new("files.layout.grid");

	public static readonly CommandId LayoutColumns =
		new("files.layout.columns");

	public static readonly CommandId SortItems =
		new("files.display.sort");

	public static readonly CommandId GroupItems =
		new("files.display.group");

	public static readonly CommandId ShowHiddenItems =
		new("files.display.show-hidden-items");

	public static readonly CommandId ShowFileExtensions =
		new("files.display.show-file-extensions");
}
