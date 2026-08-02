// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Files.Views;

public sealed partial class NavigationToolbar : UserControl
{
	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(NavigationToolbarViewModel), typeof(NavigationToolbar), new PropertyMetadata(null));

	public NavigationToolbar()
	{
		InitializeComponent();
	}

	public NavigationToolbarViewModel? ViewModel
	{
		get => (NavigationToolbarViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	private async void PathTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key is not VirtualKey.Enter)
		{
			return;
		}

		e.Handled = true;
		if (ViewModel is not { } viewModel)
		{
			return;
		}

		await viewModel.NavigatePathCommand.ExecuteAsync(PathTextBox.Text);
	}
}
