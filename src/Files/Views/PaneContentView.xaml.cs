// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class PaneContentView : UserControl
{
	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(PaneViewModel), typeof(PaneContentView), new PropertyMetadata(null, ViewModelChanged));

	public PaneViewModel? ViewModel
	{
		get => (PaneViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public PaneContentView()
	{
		InitializeComponent();
		Loaded += PaneContentView_Loaded;
	}

	private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not PaneContentView view)
		{
			return;
		}

		view.UpdateContent();
	}

	private void PaneContentView_Loaded(object sender, RoutedEventArgs e) => UpdateContent();

	private void UpdateContent() => PaneContentPresenter.Content = ViewModel?.Content;
}
