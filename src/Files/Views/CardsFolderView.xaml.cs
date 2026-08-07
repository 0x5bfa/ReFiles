// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class CardsFolderView : UserControl
{
	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(FolderBrowserViewModel), typeof(CardsFolderView), new PropertyMetadata(null, ViewModelChanged));

	private FolderViewInteraction? _interaction;

	public FolderBrowserViewModel? ViewModel
	{
		get => (FolderBrowserViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public CardsFolderView()
	{
		InitializeComponent();
		Loaded += FolderView_Loaded;
		Unloaded += FolderView_Unloaded;
	}

	private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not CardsFolderView view)
		{
			return;
		}

		view.UpdateInteraction();
	}

	private void FolderView_Loaded(object sender, RoutedEventArgs e)
	{
		UpdateInteraction();
	}

	private void FolderView_Unloaded(object sender, RoutedEventArgs e)
	{
		DisposeInteraction();
	}

	private void UpdateInteraction()
	{
		DisposeInteraction();
		if (IsLoaded && ViewModel is { } viewModel)
		{
			_interaction = new FolderViewInteraction(ItemGrid, viewModel);
		}
	}

	private void DisposeInteraction()
	{
		_interaction?.Dispose();
		_interaction = null;
	}
}
