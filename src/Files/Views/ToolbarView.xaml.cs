// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Adapters;
using Files.Commands;
using Files.Controls;
using Files.Core.Storage.Windows;
using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Files.Views;

public sealed partial class ToolbarView : UserControl
{
	private bool _isLoadingNewMenu;

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

	private async void NewMenuFlyout_Opening(object sender, object e)
	{
		if (_isLoadingNewMenu || ViewModel is not { } viewModel)
		{
			return;
		}

		_isLoadingNewMenu = true;
		NewMenuFlyout.Items.Clear();
		try
		{
			var items = await viewModel.GetNewItemsAsync();
			foreach (var item in items)
			{
				var menuItem = new MenuFlyoutItem
				{
					Text = item.Name,
					IsEnabled = item.IsEnabled,
					Tag = item,
				};
				menuItem.Icon = CreateNewItemIcon(item);

				menuItem.Click += NewMenuItem_Click;
				NewMenuFlyout.Items.Add(menuItem);
			}
		}
		catch (Exception exception)
		{
			viewModel.ReportNewMenuError(exception);
		}
		finally
		{
			_isLoadingNewMenu = false;
		}
	}

	private async void NewMenuItem_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not MenuFlyoutItem { Tag: WindowsShellNewItem item } || ViewModel is not { } viewModel)
		{
			return;
		}

		await viewModel.InvokeNewItemAsync(item);
	}

	private static IconElement? CreateNewItemIcon(WindowsShellNewItem item)
	{
		if (!item.IconData.IsEmpty)
		{
			return new ImageIcon { Source = ThumbnailImageFactory.Create(item.IconData), Width = 16, Height = 16 };
		}

		return Application.Current?.Resources.TryGetValue("App.ThemedIcons.New.Item", out var value) is true && value is ThemedIconData iconData
			? new ThemedIcon { Data = iconData, IconSize = 16 }
			: null;
	}

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
