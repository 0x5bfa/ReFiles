// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Win32.Foundation;

namespace Files.ItemProperties;

public sealed partial class ToolsPropertyView : UserControl
{
	private readonly HWND _owner;
	private readonly Action<string> _showError;

	internal WindowsShellDriveProperties Drive { get; }

	internal ToolsPropertyView(WindowsShellDriveProperties drive, HWND owner, Action<string> showError)
	{
		Drive = drive;
		_owner = owner;
		_showError = showError;
		InitializeComponent();
	}

	private void CheckButton_Click(object sender, RoutedEventArgs e)
	{
		ShowResult(WindowsShellDriveToolsService.ShowErrorChecking(_owner, Drive.RootPath));
	}

	private void OptimizeButton_Click(object sender, RoutedEventArgs e)
	{
		ShowResult(WindowsShellDriveToolsService.ShowOptimization(_owner, Drive.RootPath));
	}

	private void ShowResult(HRESULT result)
	{
		if (result.Failed)
		{
			_showError(new System.Runtime.InteropServices.COMException(null, result.Value).Message);
		}
	}
}
