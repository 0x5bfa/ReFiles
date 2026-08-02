// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class ListFolderView : UserControl
{
	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(FolderBrowserViewModel), typeof(ListFolderView), new PropertyMetadata(null, ViewModelChanged));

	private FolderViewInteraction? interaction;

	public FolderBrowserViewModel? ViewModel
	{
		get => (FolderBrowserViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public ListFolderView()
	{
		InitializeComponent();
		Loaded += FolderView_Loaded;
		Unloaded += FolderView_Unloaded;
	}

	private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not ListFolderView view)
		{
			return;
		}

		view.UpdateInteraction();
	}

	private void FolderView_Loaded(object sender, RoutedEventArgs e) =>
		UpdateInteraction();

	private void FolderView_Unloaded(object sender, RoutedEventArgs e) =>
		DisposeInteraction();

	private void UpdateInteraction()
	{
		DisposeInteraction();
		if (IsLoaded && ViewModel is { } viewModel)
		{
			interaction = new FolderViewInteraction(ItemList, viewModel);
		}
	}

	private void DisposeInteraction()
	{
		interaction?.Dispose();
		interaction = null;
	}
}
