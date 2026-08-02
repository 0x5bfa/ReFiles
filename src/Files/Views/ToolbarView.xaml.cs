// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class ToolbarView : UserControl
{
	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(ToolbarViewModel), typeof(ToolbarView), new PropertyMetadata(null));

	public ToolbarViewModel? ViewModel
	{
		get => (ToolbarViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public ToolbarView()
	{
		InitializeComponent();
	}
}
