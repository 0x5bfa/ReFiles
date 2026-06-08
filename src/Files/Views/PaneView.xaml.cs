// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class PaneView : UserControl
{
	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(
			nameof(ViewModel),
			typeof(PaneViewModel),
			typeof(PaneView),
			new PropertyMetadata(null));

	public PaneView()
	{
		InitializeComponent();
	}

	public PaneViewModel? ViewModel
	{
		get => (PaneViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public event EventHandler? Activated;

	private void Pane_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) =>
		Activated?.Invoke(this, EventArgs.Empty);
}
