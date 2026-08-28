// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Commands.Handlers;
using Files.Core.Storage.Windows;
using Files.Localization;

namespace Files.Commands;

public static class AppCommandRegistration
{
	public static CommandRegistry Build()
	{
		var builder = new CommandRegistryBuilder();
		RegisterNavigation(builder);
		RegisterFileOperations(builder);
		RegisterLayout(builder);
		RegisterDisplay(builder);
		RegisterWindow(builder);
		RegisterPane(builder);

		return builder.Build();
	}

	private static void RegisterNavigation(CommandRegistryBuilder builder)
	{
		builder.Register(new(CommandIds.ToggleSidebar, Strings.ToggleSidebar, null, Strings.Navigation, 5, "\uE700"), static _ => new SidebarCommandHandler());
		builder.Register(new(CommandIds.NavigateBack, Strings.Back, "Navigation.Back", Strings.Navigation, 10, "\uE72B"), static _ => new NavigationCommandHandler(CommandIds.NavigateBack));
		builder.Register(new(CommandIds.NavigateForward, Strings.Forward, "Navigation.Forward", Strings.Navigation, 20, "\uE72A"), static _ => new NavigationCommandHandler(CommandIds.NavigateForward));
		builder.Register(new(CommandIds.NavigateUp, Strings.Up, "Navigation.Up", Strings.Navigation, 30, "\uE74A"), static _ => new NavigationCommandHandler(CommandIds.NavigateUp));
		builder.Register(new(CommandIds.NavigateHome, Strings.Home, "Navigation.Home", Strings.Navigation, 40, "\uE80F"), static _ => new NavigationCommandHandler(CommandIds.NavigateHome));
		builder.Register(new(CommandIds.NavigatePath, Strings.Address, "Navigation.Path", Strings.Navigation, 50), static _ => new NavigationCommandHandler(CommandIds.NavigatePath));
		builder.Register(new(CommandIds.Refresh, Strings.Refresh, "Navigation.Refresh", Strings.Navigation, 60, "\uE72C"), static _ => new NavigationCommandHandler(CommandIds.Refresh));
		builder.Register(new(CommandIds.OpenItem, Strings.Open, "Item.Open", Strings.Item, 10), static root => new OpenItemCommandHandler(root.ItemActivationService));
	}

	private static void RegisterLayout(CommandRegistryBuilder builder)
	{
		builder.Register(new(CommandIds.LayoutDetails, Strings.Details, "App.ThemedIcons.IconLayout.Details.28", Strings.Layout, 10), static _ => new LayoutCommandHandler(CommandIds.LayoutDetails));
		builder.Register(new(CommandIds.LayoutList, Strings.List, "App.ThemedIcons.IconLayout.List.28", Strings.Layout, 20), static _ => new LayoutCommandHandler(CommandIds.LayoutList));
		builder.Register(new(CommandIds.LayoutCards, Strings.Cards, "App.ThemedIcons.IconLayout.Tiles.28", Strings.Layout, 30), static _ => new LayoutCommandHandler(CommandIds.LayoutCards));
		builder.Register(new(CommandIds.LayoutGrid, Strings.Grid, "App.ThemedIcons.IconLayout.Grid.28", Strings.Layout, 40), static _ => new LayoutCommandHandler(CommandIds.LayoutGrid));
		builder.Register(new(CommandIds.LayoutColumns, Strings.Columns, "App.ThemedIcons.IconLayout.Columns.28", Strings.Layout, 50), static _ => new LayoutCommandHandler(CommandIds.LayoutColumns));
	}

	private static void RegisterFileOperations(CommandRegistryBuilder builder)
	{
		builder.Register(new(CommandIds.Copy, Strings.Copy, "App.ThemedIcons.Copy", Strings.Item, 20), static _ => new FileCommandHandler(CommandIds.Copy));
		builder.Register(new(CommandIds.Cut, Strings.Cut, "App.ThemedIcons.Cut", Strings.Item, 30), static _ => new FileCommandHandler(CommandIds.Cut));
		builder.Register(new(CommandIds.Paste, Strings.Paste, "App.ThemedIcons.Paste", Strings.Item, 40), static _ => new FileCommandHandler(CommandIds.Paste));
		builder.Register(new(CommandIds.Delete, Strings.Delete, "App.ThemedIcons.Delete", Strings.Item, 50), static _ => new FileCommandHandler(CommandIds.Delete));
		builder.Register(new(CommandIds.Properties, Strings.Properties, null, Strings.Item, 60, "\uE946"), static root => new PropertiesCommandHandler(root.ItemPropertiesService));
		builder.Register(new(CommandIds.Mount, Strings.Mount, "App.ThemedIcons.File", Strings.Item, 70),
			static _ => new ContextualShellCommandHandler(CommandIds.Mount, WindowsShellContextualCommandIds.Mount));
		builder.Register(new(CommandIds.BurnDiscImage, Strings.BurnDiscImage, "App.ThemedIcons.File", Strings.Item, 80),
			static _ => new ContextualShellCommandHandler(CommandIds.BurnDiscImage, WindowsShellContextualCommandIds.BurnDiscImage));
		builder.Register(new(CommandIds.EmptyRecycleBin, Strings.EmptyRecycleBin, "App.ThemedIcons.Delete", Strings.Item, 90),
			static _ => new ContextualShellCommandHandler(CommandIds.EmptyRecycleBin, WindowsShellContextualCommandIds.EmptyRecycleBin));
		builder.Register(new(CommandIds.RestoreAllRecycleBinItems, Strings.RestoreAllItems, "App.ThemedIcons.RestoreDeleted", Strings.Item, 100),
			static _ => new ContextualShellCommandHandler(CommandIds.RestoreAllRecycleBinItems, WindowsShellContextualCommandIds.RestoreAllRecycleBinItems));
		builder.Register(new(CommandIds.RestoreRecycleBinItems, Strings.RestoreSelectedItems, "App.ThemedIcons.RestoreDeleted", Strings.Item, 110),
			static _ => new ContextualShellCommandHandler(CommandIds.RestoreRecycleBinItems, WindowsShellContextualCommandIds.RestoreRecycleBinItems));
		builder.Register(new(CommandIds.CompressToZip, Strings.CompressToZip, "App.ThemedIcons.Zip", Strings.Item, 120),
			static _ => new ContextualShellCommandHandler(CommandIds.CompressToZip, WindowsShellContextualCommandIds.CompressToZip));
		builder.Register(new(CommandIds.PinToQuickAccess, Strings.PinToQuickAccess, "App.ThemedIcons.Folder", Strings.Item, 130),
			static _ => new ContextualShellCommandHandler(CommandIds.PinToQuickAccess, WindowsShellContextualCommandIds.PinToQuickAccess));
		builder.Register(new(CommandIds.AddToFavorites, Strings.AddToFavorites, "App.ThemedIcons.Tag", Strings.Item, 140),
			static _ => new ContextualShellCommandHandler(CommandIds.AddToFavorites, WindowsShellContextualCommandIds.AddToFavorites));
		builder.Register(new(CommandIds.CopyAsPath, Strings.CopyAsPath, "App.ThemedIcons.CopyAsPath", Strings.Item, 150),
			static _ => new ContextualShellCommandHandler(CommandIds.CopyAsPath, WindowsShellContextualCommandIds.CopyAsPath));
	}

	private static void RegisterDisplay(CommandRegistryBuilder builder)
	{
		builder.Register(new(CommandIds.SortItems, Strings.Sort, "Display.Sort", Strings.Layout, 40, "\uE8CB"), static _ => new DisplayCommandHandler(CommandIds.SortItems));
		builder.Register(new(CommandIds.GroupItems, Strings.GroupBy, "Display.Group", Strings.Layout, 50, "\uE902"), static _ => new DisplayCommandHandler(CommandIds.GroupItems));
		builder.Register(new(CommandIds.ShowHiddenItems, Strings.ShowHiddenItems, null, Strings.Layout, 60), static _ => new DisplayCommandHandler(CommandIds.ShowHiddenItems));
		builder.Register(new(CommandIds.ShowFileExtensions, Strings.ShowFileExtensions, null, Strings.Layout, 70), static _ => new DisplayCommandHandler(CommandIds.ShowFileExtensions));
	}

	private static void RegisterWindow(CommandRegistryBuilder builder)
	{
		builder.Register(new(CommandIds.NewTab, Strings.NewTab, "Tab.New", Strings.Tabs, 10), static _ => new WindowCommandHandler(CommandIds.NewTab));
		builder.Register(new(CommandIds.DuplicateTab, Strings.DuplicateTab, "Tab.Duplicate", Strings.Tabs, 20), static _ => new WindowCommandHandler(CommandIds.DuplicateTab));
		builder.Register(
			new(CommandIds.MoveTabToNewWindow, Strings.MoveTabToNewWindow, "App.ThemedIcons.OpenInWindow", Strings.Tabs, 30),
			static _ => new WindowCommandHandler(CommandIds.MoveTabToNewWindow));
		builder.Register(new(CommandIds.CloseTabsToLeft, Strings.CloseTabsToLeft, null, Strings.Tabs, 40), static _ => new WindowCommandHandler(CommandIds.CloseTabsToLeft));
		builder.Register(new(CommandIds.CloseTabsToRight, Strings.CloseTabsToRight, null, Strings.Tabs, 50), static _ => new WindowCommandHandler(CommandIds.CloseTabsToRight));
		builder.Register(new(CommandIds.CloseOtherTabs, Strings.CloseOtherTabs, null, Strings.Tabs, 60), static _ => new WindowCommandHandler(CommandIds.CloseOtherTabs));
		builder.Register(new(CommandIds.ReopenTab, Strings.ReopenTab, null, Strings.Tabs, 70), static _ => new WindowCommandHandler(CommandIds.ReopenTab));
		builder.Register(new(CommandIds.CloseTab, Strings.Close, "Tab.Close", Strings.Tabs, 80), static _ => new WindowCommandHandler(CommandIds.CloseTab));
	}

	private static void RegisterPane(CommandRegistryBuilder builder)
	{
		builder.Register(new(CommandIds.NewPane, Strings.NewPane, "Pane.New", Strings.Panes, 10), static _ => new PaneCommandHandler(CommandIds.NewPane));
		builder.Register(new(CommandIds.ClosePane, Strings.ClosePane, "Pane.Close", Strings.Panes, 20), static _ => new PaneCommandHandler(CommandIds.ClosePane));
		builder.Register(
			new(CommandIds.SplitPaneVertical, Strings.SplitVertically, "App.ThemedIcons.Panes.Vertical", Strings.Panes, 30),
			static _ => new PaneCommandHandler(CommandIds.SplitPaneVertical));
		builder.Register(
			new(CommandIds.SplitPaneHorizontal, Strings.SplitHorizontally, "App.ThemedIcons.Panes.Horizontal", Strings.Panes, 40),
			static _ => new PaneCommandHandler(CommandIds.SplitPaneHorizontal));
	}
}
