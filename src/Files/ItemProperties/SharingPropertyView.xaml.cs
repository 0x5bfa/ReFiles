// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using Files.Adapters;
using Files.Core.Storage.Windows;
using Files.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.Win32.Foundation;

namespace Files.ItemProperties;

public sealed partial class SharingPropertyView : UserControl
{
	private const int OperationCancelled = unchecked((int)0x800704C7);
	private const int OperationTimedOut = unchecked((int)0x800705B4);
	private readonly HWND _owner;
	private readonly Action<string> _showError;

	internal WindowsShellSharingProperties Sharing { get; }

	internal Visibility NetworkPathVisibility => Sharing.IsShared ? Visibility.Visible : Visibility.Collapsed;

	internal string PasswordProtectionDescription => Sharing.IsPasswordProtectionEnabled
		? Strings.PasswordProtectionDescription.GetLocalized()
		: Strings.PasswordProtectionDisabledDescription.GetLocalized();

	internal Visibility PasswordProtectionVisibility => Sharing.ShowPasswordProtection ? Visibility.Visible : Visibility.Collapsed;

	internal string SharingState => Sharing.IsShared ? Strings.Shared.GetLocalized() : Strings.NotShared.GetLocalized();

	internal SharingPropertyView(WindowsShellSharingProperties sharing, HWND owner, Action<string> showError)
	{
		Sharing = sharing;
		_owner = owner;
		_showError = showError;
		InitializeComponent();
		_ = LoadFolderIconAsync();
	}

	private void AdvancedSharingButton_Click(object sender, RoutedEventArgs e)
	{
		RunSharingAction(() => WindowsShellSharingService.ShowAdvancedSharing(_owner, Sharing.ObjectPath));
	}

	private void NetworkAndSharingCenterLink_Click(Hyperlink sender, RoutedEventArgs args)
	{
		RunSharingAction(WindowsShellSharingService.OpenNetworkAndSharingCenter);
	}

	private async Task LoadFolderIconAsync()
	{
		var iconData = WindowsShellIconProvider.GetFileSystemIcon(Sharing.ObjectPath);
		if (!iconData.IsEmpty)
		{
			FolderIconImage.Source = await ThumbnailImageFactory.CreateAsync(iconData);
		}
	}

	private void RunSharingAction(Func<HRESULT> action)
	{
		var result = action();
		if (result.Succeeded || result.Value is OperationCancelled or OperationTimedOut)
		{
			return;
		}

		_showError(Marshal.GetExceptionForHR(result.Value)?.Message ?? result.ToString());
	}

	private void ShareButton_Click(object sender, RoutedEventArgs e)
	{
		RunSharingAction(() => WindowsShellSharingService.ShowSharingWizard(_owner, Sharing.ObjectPath));
	}
}
