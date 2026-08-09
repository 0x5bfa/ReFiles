// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Commands;
using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

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

	private void LayoutButton_Click(object sender, RoutedEventArgs e) =>
		LayoutFlyout.Hide();

	private void LayoutSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
	{
		ViewModel?.SetLayoutSize(e.NewValue);
	}

	private void ShowHiddenItemsToggleSwitch_Toggled(object sender, RoutedEventArgs e) =>
		ExecuteToggleCommand(sender, ViewModel?.ShowHiddenItemsCommand);

	private void ShowFileExtensionsToggleSwitch_Toggled(object sender, RoutedEventArgs e) =>
		ExecuteToggleCommand(sender, ViewModel?.ShowFileExtensionsCommand);

	private static void ExecuteToggleCommand(object sender, CommandBindingViewModel? command)
	{
		if (sender is not ToggleSwitch toggleSwitch)
		{
			return;
		}

		if (command is null)
		{
			return;
		}

		if (toggleSwitch.IsOn == command.IsChecked)
		{
			return;
		}

		command.Command.Execute(null);
	}
}
