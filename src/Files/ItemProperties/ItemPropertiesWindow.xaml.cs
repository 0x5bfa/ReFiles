// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Files.ItemProperties;

public sealed partial class ItemPropertiesWindow : Window
{
	internal ItemPropertiesViewModel ViewModel { get; }

	internal ItemPropertiesWindow(IReadOnlyList<BrowseItemViewModel> items)
	{
		ViewModel = new(items);
		InitializeComponent();
		Title = ViewModel.WindowTitle;
		AppWindow.Resize(new SizeInt32(520, 440));
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
