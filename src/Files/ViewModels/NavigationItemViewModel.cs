// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Core.Storage;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Files.ViewModels;

public sealed class NavigationItemViewModel : ObservableObject
{
	private readonly bool prefersThumbnail;
	private BitmapImage? thumbnail;

	private NavigationItemViewModel(
		string name,
		StorableReference? reference,
		bool isHome,
		bool selectsOnInvoked,
		IconElement icon,
		IEnumerable<NavigationItemViewModel>? children = null,
		bool prefersThumbnail = false)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(icon);

		Name = name;
		Reference = reference;
		IsHome = isHome;
		SelectsOnInvoked = selectsOnInvoked;
		Icon = icon;
		this.prefersThumbnail = prefersThumbnail;
		Children = children is null
			? []
			: new ObservableCollection<NavigationItemViewModel>(children);
	}

	public string Name { get; }

	public StorableReference? Reference { get; }

	public bool IsHome { get; }

	public bool SelectsOnInvoked { get; }

	public ObservableCollection<NavigationItemViewModel> Children { get; }

	public IconElement Icon { get; private set; }

	public BitmapImage? Thumbnail
	{
		get => thumbnail;
		private set => SetProperty(ref thumbnail, value);
	}

	internal static NavigationItemViewModel CreateHome(string name) =>
		new(name, reference: null, isHome: true, selectsOnInvoked: true, icon: new SymbolIcon {Symbol = Symbol.Home});

	internal static NavigationItemViewModel CreateSection(string name, StorableReference reference, IEnumerable<NavigationItemViewModel> children) =>
		new(name, reference, isHome: false, selectsOnInvoked: false, icon: new SymbolIcon {Symbol = Symbol.Folder}, children: children);

	internal static NavigationItemViewModel CreateFolder(string name, StorableReference reference) =>
		new(name, reference, isHome: false, selectsOnInvoked: true, icon: new SymbolIcon {Symbol = Symbol.Folder}, prefersThumbnail: true);

	internal void SetThumbnail(BitmapImage? value)
	{
		Thumbnail = value;
		if (!prefersThumbnail || value is null)
		{
			return;
		}

		Icon = new ImageIcon { Source = value };
		OnPropertyChanged(nameof(Icon));
	}
}
