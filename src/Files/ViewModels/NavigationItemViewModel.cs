// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Core.Storage;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Files.ViewModels;

public sealed partial class NavigationItemViewModel : ObservableObject
{
	private readonly bool _prefersThumbnail;

	public string Name { get; }

	public StorableReference? Reference { get; }

	public bool IsHome { get; }

	public bool SelectsOnInvoked { get; }

	public ObservableCollection<NavigationItemViewModel> Children { get; }

	public IconElement Icon { get; private set; }

	[ObservableProperty]
	public partial BitmapImage? Thumbnail { get; set; }

	private NavigationItemViewModel(string name, StorableReference? reference, bool isHome, bool selectsOnInvoked, IconElement icon, IEnumerable<NavigationItemViewModel>? children = null, bool prefersThumbnail = false)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(icon);

		Name = name;
		Reference = reference;
		IsHome = isHome;
		SelectsOnInvoked = selectsOnInvoked;
		Icon = icon;
		_prefersThumbnail = prefersThumbnail;
		Children = children is null ? [] : new ObservableCollection<NavigationItemViewModel>(children);
	}

	internal static NavigationItemViewModel CreateHome(string name)
	{
		return new(name, reference: null, true, true, new SymbolIcon() { Symbol = Symbol.Home });
	}

	internal static NavigationItemViewModel CreateSection(string name, StorableReference reference, IEnumerable<NavigationItemViewModel> children)
	{
		return new(name, reference, false, false, new SymbolIcon() { Symbol = Symbol.Folder }, children: children);
	}

	internal static NavigationItemViewModel CreateFolder(string name, StorableReference reference)
	{
		return new(name, reference, false, true, new SymbolIcon() { Symbol = Symbol.Folder }, prefersThumbnail: true);
	}

	internal void SetThumbnail(BitmapImage? value)
	{
		Thumbnail = value;
		if (!_prefersThumbnail || value is null)
		{
			return;
		}

		Icon = new ImageIcon { Source = value };
		OnPropertyChanged(nameof(Icon));
	}
}
