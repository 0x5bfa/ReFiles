// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Adapters;
using Files.Core.Storage.Windows;
using Files.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.Win32.Foundation;

namespace Files.ItemProperties;

public sealed partial class CustomizePropertyView : UserControl
{
	private const string GenericFolderKind = "Generic";
	private readonly ItemPropertiesViewModel _ownerViewModel;
	private readonly HWND _owner;
	private readonly Action<string> _showError;
	private string _appliedFolderKind;
	private string _appliedPicturePath;
	private string _appliedIconPath;
	private int _appliedIconIndex;
	private string _selectedFolderKind;
	private string _picturePath;
	private string _iconPath;
	private int _iconIndex;
	private bool _applyToSubfolders;
	private bool _isInitializing;

	internal WindowsShellFolderCustomizationProperties Customization { get; }

	internal IReadOnlyList<FolderKindOption> FolderKinds { get; }

	internal bool HasChanges => !StringComparer.OrdinalIgnoreCase.Equals(_selectedFolderKind, _appliedFolderKind)
		|| !StringComparer.OrdinalIgnoreCase.Equals(_picturePath, _appliedPicturePath)
		|| !StringComparer.OrdinalIgnoreCase.Equals(_iconPath, _appliedIconPath) || _iconIndex != _appliedIconIndex;

	internal CustomizePropertyView(WindowsShellFolderCustomizationProperties customization, ItemPropertiesViewModel ownerViewModel, HWND owner, Action<string> showError)
	{
		Customization = customization;
		_ownerViewModel = ownerViewModel;
		_owner = owner;
		_showError = showError;
		_appliedFolderKind = NormalizeFolderKind(customization.FolderKind);
		_appliedPicturePath = customization.PicturePath;
		_appliedIconPath = customization.IconPath;
		_appliedIconIndex = customization.IconIndex;
		_selectedFolderKind = _appliedFolderKind;
		_picturePath = _appliedPicturePath;
		_iconPath = _appliedIconPath;
		_iconIndex = _appliedIconIndex;
		_applyToSubfolders = customization.ApplyToSubfolders;
		FolderKinds = CreateFolderKinds(_appliedFolderKind);
		_isInitializing = true;
		InitializeComponent();
		FolderKindComboBox.SelectedItem = FolderKinds.First(static option => option.IsSelected);
		ApplyToSubfoldersCheckBox.IsChecked = _applyToSubfolders;
		_isInitializing = false;
	}

	internal void Apply()
	{
		if (!HasChanges)
		{
			return;
		}

		WindowsShellFolderCustomizationService.Apply(
			Customization.ObjectPath, _selectedFolderKind, !StringComparer.OrdinalIgnoreCase.Equals(_selectedFolderKind, _appliedFolderKind), _applyToSubfolders,
			_picturePath, !StringComparer.OrdinalIgnoreCase.Equals(_picturePath, _appliedPicturePath), _iconPath, _iconIndex,
			!StringComparer.OrdinalIgnoreCase.Equals(_iconPath, _appliedIconPath) || _iconIndex != _appliedIconIndex);
		_appliedFolderKind = _selectedFolderKind;
		_appliedPicturePath = _picturePath;
		_appliedIconPath = _iconPath;
		_appliedIconIndex = _iconIndex;
		SetHasChanges();
	}

	private static IReadOnlyList<FolderKindOption> CreateFolderKinds(string selectedFolderKind)
	{
		var options = new List<FolderKindOption>
		{
			new(GenericFolderKind, Strings.GeneralItems.GetLocalized(), selectedFolderKind.Equals(GenericFolderKind, StringComparison.OrdinalIgnoreCase)),
			new("Documents", Strings.Documents.GetLocalized(), selectedFolderKind.Equals("Documents", StringComparison.OrdinalIgnoreCase)),
			new("Pictures", Strings.Pictures.GetLocalized(), selectedFolderKind.Equals("Pictures", StringComparison.OrdinalIgnoreCase)),
			new("Music", Strings.Music.GetLocalized(), selectedFolderKind.Equals("Music", StringComparison.OrdinalIgnoreCase)),
			new("Videos", Strings.Videos.GetLocalized(), selectedFolderKind.Equals("Videos", StringComparison.OrdinalIgnoreCase)),
		};
		if (!options.Any(static option => option.IsSelected))
		{
			options.Insert(0, new(selectedFolderKind, selectedFolderKind, true));
		}

		return options;
	}

	private static string NormalizeFolderKind(string folderKind)
	{
		return string.IsNullOrWhiteSpace(folderKind) ? GenericFolderKind : folderKind;
	}

	private async void UserControl_Loaded(object sender, RoutedEventArgs e)
	{
		await UpdateIconPreviewAsync();
	}

	private void FolderKindComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_isInitializing || FolderKindComboBox.SelectedItem is not FolderKindOption option)
		{
			return;
		}

		_selectedFolderKind = option.CanonicalName;
		SetHasChanges();
	}

	private void ApplyToSubfoldersCheckBox_Click(object sender, RoutedEventArgs e)
	{
		_applyToSubfolders = ApplyToSubfoldersCheckBox.IsChecked is true;
	}

	private async void ChooseFileButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
			picker.FileTypeFilter.Add(".bmp");
			picker.FileTypeFilter.Add(".gif");
			picker.FileTypeFilter.Add(".jpeg");
			picker.FileTypeFilter.Add(".jpg");
			picker.FileTypeFilter.Add(".png");
			WinRT.Interop.InitializeWithWindow.Initialize(picker, GetHandleValue(_owner));
			var file = await picker.PickSingleFileAsync();
			if (file is null)
			{
				return;
			}

			_picturePath = file.Path;
			SetHasChanges();
		}
		catch (Exception exception)
		{
			_showError(exception.Message);
		}
	}

	private void RestorePictureButton_Click(object sender, RoutedEventArgs e)
	{
		_picturePath = string.Empty;
		SetHasChanges();
	}

	private async void ChangeIconButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			var initialPath = string.IsNullOrWhiteSpace(_iconPath) ? Path.Combine(Environment.SystemDirectory, "shell32.dll") : _iconPath;
			var initialIndex = string.IsNullOrWhiteSpace(_iconPath) ? 3 : _iconIndex;
			if (!WindowsShellFolderCustomizationService.TryPickIcon(_owner, initialPath, initialIndex, out var selectedPath, out var selectedIndex))
			{
				return;
			}

			_iconPath = selectedPath.Equals(Path.Combine(Environment.SystemDirectory, "shell32.dll"), StringComparison.OrdinalIgnoreCase) && selectedIndex is 3
				? string.Empty
				: selectedPath;
			_iconIndex = string.IsNullOrEmpty(_iconPath) ? 0 : selectedIndex;
			SetHasChanges();
			await UpdateIconPreviewAsync();
		}
		catch (Exception exception)
		{
			_showError(exception.Message);
		}
	}

	private void SetHasChanges()
	{
		_ownerViewModel.SetPropertyPageChanges(HasChanges);
	}

	private static unsafe nint GetHandleValue(HWND window)
	{
		return (nint)window.Value;
	}

	private async Task UpdateIconPreviewAsync()
	{
		var iconData = string.IsNullOrWhiteSpace(_iconPath)
			? WindowsShellIconProvider.GetFileSystemIcon(Customization.ObjectPath, 48)
			: WindowsShellIconProvider.GetResourceIcon(_iconPath, _iconIndex);
		if (iconData.IsEmpty)
		{
			iconData = WindowsShellIconProvider.GetFolderIcon(48);
		}

		FolderIconImage.Source = await ThumbnailImageFactory.CreateAsync(iconData);
	}
}

internal sealed class FolderKindOption
{
	public string CanonicalName { get; }

	public string DisplayName { get; }

	public bool IsSelected { get; }

	internal FolderKindOption(string canonicalName, string displayName, bool isSelected)
	{
		CanonicalName = canonicalName;
		DisplayName = displayName;
		IsSelected = isSelected;
	}
}
