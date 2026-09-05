// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using Files.Core.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.ItemProperties;

public sealed partial class GeneralPropertyView : UserControl
{
	private readonly Action<string> _showError;

	internal ItemPropertiesViewModel ViewModel { get; }

	internal GeneralPropertyView(ItemPropertiesViewModel viewModel, Action<string> showError)
	{
		ViewModel = viewModel;
		_showError = showError;
		InitializeComponent();
	}

	private void StorageDetailsButton_Click(object sender, RoutedEventArgs e)
	{
		if (ViewModel.PrimaryPath is not { } rootPath)
		{
			return;
		}

		var result = WindowsShellStorageSettingsService.OpenDriveUsage(rootPath);
		if (result.Failed)
		{
			_showError(new COMException(null, result.Value).Message);
		}
	}

	private async void AdvancedButton_Click(object sender, RoutedEventArgs e)
	{
		var dialog = new AdvancedAttributesDialog(ViewModel, _showError) { XamlRoot = XamlRoot };
		if (await dialog.ShowAsync() is ContentDialogResult.Primary)
		{
			dialog.Commit();
		}
	}
}
