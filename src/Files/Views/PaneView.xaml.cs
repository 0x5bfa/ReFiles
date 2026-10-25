// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class PaneView : UserControl
{
	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(PaneViewModel), typeof(PaneView), new PropertyMetadata(null));

	public PaneViewModel? ViewModel
	{
		get => (PaneViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public event EventHandler? Activated;

	public PaneView()
	{
		InitializeComponent();
	}

	internal void SetShadow(bool isActive, bool isMultiPane)
	{
		var hasChrome = ViewModel?.HasPaneChrome is true;
		PaneBorder.BorderThickness = new Thickness(hasChrome ? 1 : 0);
		StatusBar.Visibility = hasChrome ? Visibility.Visible : Visibility.Collapsed;
		PaneBorder.Translation = new System.Numerics.Vector3(0, 0, hasChrome && isActive ? (isMultiPane ? 32 : 8) : 0);
	}

	private void Pane_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
	{
		Activated?.Invoke(this, EventArgs.Empty);
	}
}
