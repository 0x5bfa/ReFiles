// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Commands.Handlers;
using Files.Localization;

namespace Files.Commands;

public static class AppCommandRegistration
{
	public static CommandRegistry Build()
	{
		var builder = new CommandRegistryBuilder();
		RegisterNavigation(builder);
		RegisterWindow(builder);
		RegisterPane(builder);
		return builder.Build();
	}

	private static void RegisterNavigation(CommandRegistryBuilder builder)
	{
		builder.Register(
			new(CommandIds.NavigateBack, Strings.Back, "Navigation.Back", Strings.Navigation, 10),
			static _ => new NavigationCommandHandler(CommandIds.NavigateBack));
		builder.Register(
			new(CommandIds.NavigateForward, Strings.Forward, "Navigation.Forward", Strings.Navigation, 20),
			static _ => new NavigationCommandHandler(CommandIds.NavigateForward));
		builder.Register(
			new(CommandIds.NavigateUp, Strings.Up, "Navigation.Up", Strings.Navigation, 30),
			static _ => new NavigationCommandHandler(CommandIds.NavigateUp));
		builder.Register(
			new(CommandIds.NavigateHome, Strings.Home, "Navigation.Home", Strings.Navigation, 40),
			static _ => new NavigationCommandHandler(CommandIds.NavigateHome));
		builder.Register(
			new(CommandIds.NavigatePath, Strings.Address, "Navigation.Path", Strings.Navigation, 50),
			static _ => new NavigationCommandHandler(CommandIds.NavigatePath));
		builder.Register(
			new(CommandIds.Refresh, Strings.Refresh, "Navigation.Refresh", Strings.Navigation, 60),
			static _ => new NavigationCommandHandler(CommandIds.Refresh));
		builder.Register(
			new(CommandIds.OpenItem, Strings.Open, "Item.Open", Strings.Item, 10),
			static _ => new NavigationCommandHandler(CommandIds.OpenItem));
	}

	private static void RegisterWindow(CommandRegistryBuilder builder)
	{
		builder.Register(new(CommandIds.NewTab, Strings.NewTab, "Tab.New", Strings.Tabs, 10), static _ => new WindowCommandHandler(CommandIds.NewTab));
		builder.Register(new(CommandIds.CloseTab, Strings.Close, "Tab.Close", Strings.Tabs, 20), static _ => new WindowCommandHandler(CommandIds.CloseTab));
	}

	private static void RegisterPane(CommandRegistryBuilder builder)
	{
		builder.Register(new(CommandIds.NewPane, Strings.NewPane, "Pane.New", Strings.Panes, 10), static _ => new PaneCommandHandler(CommandIds.NewPane));
		builder.Register(
			new(CommandIds.ClosePane, Strings.ClosePane, "Pane.Close", Strings.Panes, 20),
			static _ => new PaneCommandHandler(CommandIds.ClosePane));
	}
}
