// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Localization;
using Files.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class SettingsView : UserControl
{
	private readonly SettingsNavigationItem _generalItem;
	private readonly SettingsNavigationItem _appearanceItem;
	private readonly SettingsNavigationItem _aboutItem;
	private readonly GeneralSettingsView _generalView;
	private readonly AppearanceSettingsView _appearanceView;
	private readonly AboutSettingsView _aboutView;

	internal ObservableCollection<SettingsNavigationItem> NavigationItems { get; }

	internal ObservableCollection<SettingsNavigationItem> FooterNavigationItems { get; }

	public SettingsView() : this(((App)Application.Current).Settings)
	{
	}

	internal SettingsView(AppSettingsService settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_generalItem = new(SettingsPageKind.General, Strings.General.GetLocalized(), "\uE713");
		_appearanceItem = new(SettingsPageKind.Appearance, Strings.Appearance.GetLocalized(), "\uE790");
		_aboutItem = new(SettingsPageKind.About, Strings.About.GetLocalized(), "\uE946");
		NavigationItems = [_generalItem, _appearanceItem];
		FooterNavigationItems = [_aboutItem];
		_generalView = new(settings);
		_appearanceView = new(settings);
		_aboutView = new();
		InitializeComponent();
		SettingsNavigation.SelectedItem = _generalItem;
		SettingsContent.Content = _generalView;
	}

	private void SettingsNavigation_ItemInvoked(object? sender, Files.Controls.ItemInvokedEventArgs e)
	{
		if (SettingsNavigation.SelectedItem is not SettingsNavigationItem item)
		{
			return;
		}

		SettingsContent.Content = item.PageKind switch
		{
			SettingsPageKind.Appearance => _appearanceView,
			SettingsPageKind.About => _aboutView,
			_ => _generalView,
		};
	}
}

internal enum SettingsPageKind
{
	General,
	Appearance,
	About,
}

internal sealed record SettingsNavigationItem(SettingsPageKind PageKind, string Text, string Glyph);
