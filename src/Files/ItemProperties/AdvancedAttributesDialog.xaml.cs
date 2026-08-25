// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.IO;
using Files.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.ItemProperties;

internal sealed partial class AdvancedAttributesDialog : ContentDialog
{
	private readonly Action<string> _showError;
	private readonly bool _wasEncrypted;
	private bool _isInitializing;

	internal ItemPropertiesViewModel ViewModel { get; }

	internal bool AllowMixedStates => ViewModel.IsArchive is null || ViewModel.IsIndexed is null || ViewModel.IsCompressed is null || ViewModel.IsEncrypted is null;

	internal string ArchiveAndIndexSectionLabel => ViewModel.IsSingleFile ? Strings.FileAttributes.GetLocalized() : Strings.ArchiveAndIndexAttributes.GetLocalized();

	internal string ArchiveLabel => ViewModel.IsSingleFile ? Strings.FileReadyForArchiving.GetLocalized() : Strings.FolderReadyForArchiving.GetLocalized();

	internal string Description => ViewModel.IsSingleFile
		? Strings.AdvancedAttributesFileDescription.GetLocalized()
		: ViewModel.IsSingleFolder
			? Strings.AdvancedAttributesFolderDescription.GetLocalized()
			: Strings.AdvancedAttributesSelectionDescription.GetLocalized();

	internal string IndexLabel => ViewModel.IsSingleFile ? Strings.AllowFileIndexing.GetLocalized() : Strings.AllowFolderIndexing.GetLocalized();

	internal AdvancedAttributesDialog(ItemPropertiesViewModel viewModel, Action<string> showError)
	{
		ViewModel = viewModel;
		_showError = showError;
		_wasEncrypted = viewModel.IsEncrypted is true;
		InitializeComponent();
		_isInitializing = true;
		ArchiveCheckBox.IsChecked = viewModel.IsArchive;
		IndexCheckBox.IsChecked = viewModel.IsIndexed;
		CompressCheckBox.IsChecked = viewModel.IsCompressed;
		EncryptCheckBox.IsChecked = viewModel.IsEncrypted;
		DetailsButton.IsEnabled = _wasEncrypted && viewModel.IsSingleFile;
		_isInitializing = false;
	}

	internal void Commit()
	{
		ViewModel.IsArchive = ArchiveCheckBox.IsChecked;
		ViewModel.IsIndexed = IndexCheckBox.IsChecked;
		ViewModel.IsCompressed = CompressCheckBox.IsChecked;
		ViewModel.IsEncrypted = EncryptCheckBox.IsChecked;
	}

	private void CompressCheckBox_Checked(object sender, RoutedEventArgs e)
	{
		if (!_isInitializing)
		{
			EncryptCheckBox.IsChecked = false;
		}
	}

	private void DetailsButton_Click(object sender, RoutedEventArgs e)
	{
		if (!_wasEncrypted || ViewModel.PrimaryPath is not { } path)
		{
			return;
		}

		try
		{
			var startInfo = new ProcessStartInfo { FileName = Path.Combine(Environment.SystemDirectory, "rundll32.exe"), UseShellExecute = true };
			startInfo.ArgumentList.Add("efsadu.dll,EfsDetail");
			startInfo.ArgumentList.Add(path);
			Process.Start(startInfo);
		}
		catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			_showError(exception.Message);
		}
	}

	private void EncryptCheckBox_Checked(object sender, RoutedEventArgs e)
	{
		if (!_isInitializing)
		{
			CompressCheckBox.IsChecked = false;
		}
	}
}
