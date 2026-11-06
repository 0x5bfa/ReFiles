// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Win32.Foundation;

namespace Files.ItemProperties;

public sealed partial class ToolsPropertyView : UserControl
{
	private readonly IWindowsShellPropertyActions _propertyActions;
	private readonly HWND _owner;
	private readonly Action<string> _showError;

	internal WindowsShellDriveProperties Drive { get; }

	internal ToolsPropertyView(IWindowsShellPropertyActions propertyActions, WindowsShellDriveProperties drive, HWND owner, Action<string> showError, ImageSource? elevationShield)
	{
		Drive = drive;
		_owner = owner;
		_showError = showError;
		_propertyActions = propertyActions;
		InitializeComponent();
		ElevationShield.Source = elevationShield;
	}

	private void CheckButton_Click(object sender, RoutedEventArgs e)
	{
		ShowResult(_propertyActions.ShowErrorChecking(_owner, Drive.RootPath));
	}

	private void OptimizeButton_Click(object sender, RoutedEventArgs e)
	{
		ShowResult(_propertyActions.ShowOptimization(_owner, Drive.RootPath));
	}

	private void ShowResult(WindowsShellOperationResult result)
	{
		if (result.Failed)
		{
			_showError(new System.Runtime.InteropServices.COMException(null, result.HResult).Message);
		}
	}
}
