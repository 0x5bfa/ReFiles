// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Controls;
using Files.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Files.ViewModels;

public sealed partial class NavigationItemViewModel : ObservableObject
{
	private const double FolderIconFontSize = 14;

	private const string FolderIconGlyph = "\uE8B7";
	private const string SettingsIconResourceKey = "App.ThemedIcons.Settings";
	private const string SettingsIconGlyph = "\uE713";

	private readonly bool _prefersThumbnail;

	private IconElement _icon;

	private bool _isExpanded = true;

	public string Name { get; }

	public StorableReference? Reference { get; }

	public bool IsHome { get; }

	public bool SelectsOnInvoked { get; }

	public ObservableCollection<NavigationItemViewModel> Children { get; }

	public IconElement Icon => CreateIconElement();

	public object? MenuItemsSource => SelectsOnInvoked && Children.Count is 0 ? null : Children;

	public bool IsExpanded
	{
		get => _isExpanded;
		set => SetProperty(ref _isExpanded, value);
	}

	public string Text => Name;

	public object ToolTip => Name;

	[ObservableProperty]
	public partial BitmapImage? Thumbnail { get; set; }

	private NavigationItemViewModel(string name, StorableReference? reference, bool isHome, bool selectsOnInvoked, IconElement icon,
		IEnumerable<NavigationItemViewModel>? children = null, bool prefersThumbnail = false)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(icon);

		Name = name;
		Reference = reference;
		IsHome = isHome;
		SelectsOnInvoked = selectsOnInvoked;
		_icon = icon;
		_prefersThumbnail = prefersThumbnail;
		Children = children is null ? [] : new ObservableCollection<NavigationItemViewModel>(children);
	}

	internal static NavigationItemViewModel CreateHome(string name)
	{
		return new(name, reference: null, true, true, CreateSectionIcon(SidebarSectionType.Home));
	}

	internal static NavigationItemViewModel CreateSettings(string name)
	{
		return new(name, reference: null, false, true, CreateSettingsIcon());
	}

	internal static NavigationItemViewModel CreateSection(SidebarSectionType sectionType, string name, StorableReference reference, IEnumerable<NavigationItemViewModel> children)
	{
		return new(name, reference, false, false, CreateSectionIcon(sectionType), children: children);
	}

	internal static NavigationItemViewModel CreateFolder(string name, StorableReference reference)
	{
		return new(name, reference, false, true, new FontIcon { FontSize = FolderIconFontSize, Glyph = FolderIconGlyph }, prefersThumbnail: true);
	}

	internal void SetThumbnail(BitmapImage? value)
	{
		Thumbnail = value;
		if (!_prefersThumbnail || value is null)
		{
			return;
		}

		_icon = new ImageIcon { Source = value };
		OnPropertyChanged(nameof(Icon));
	}

	private IconElement CreateIconElement()
	{
		return _icon switch
		{
			SymbolIcon symbolIcon => new SymbolIcon { Symbol = symbolIcon.Symbol },
			FontIcon fontIcon => new FontIcon { FontSize = fontIcon.FontSize, Glyph = fontIcon.Glyph },
			ImageIcon imageIcon => new ImageIcon { Source = imageIcon.Source },
			ThemedIcon themedIcon => new ThemedIcon { Data = themedIcon.Data, IconSize = themedIcon.IconSize },
			_ => throw new InvalidOperationException($"Unsupported navigation icon type '{_icon.GetType().Name}'."),
		};
	}

	private static IconElement CreateSettingsIcon()
	{
		return Application.Current?.Resources.TryGetValue(SettingsIconResourceKey, out var value) is true && value is ThemedIconData iconData
			? new ThemedIcon { Data = iconData, IconSize = 16 }
			: new FontIcon { Glyph = SettingsIconGlyph };
	}

	private static IconElement CreateSectionIcon(SidebarSectionType sectionType)
	{
		return new ImageIcon { Source = new BitmapImage(new Uri(SidebarSectionIcons.For(sectionType))) };
	}
}
