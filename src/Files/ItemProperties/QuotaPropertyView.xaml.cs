// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Files.Core.Storage.Windows;
using Files.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Win32.Foundation;

namespace Files.ItemProperties;

public sealed partial class QuotaPropertyView : UserControl
{
	private readonly HWND _owner;
	private readonly Action<string> _showError;

	internal WindowsShellQuotaProperties Quota { get; }

	internal string DefaultLimit => FormatQuotaBytes(Quota.DefaultLimit);

	internal string DefaultThreshold => FormatQuotaBytes(Quota.DefaultThreshold);

	internal Visibility ElevationVisibility => Quota.RequiresElevation ? Visibility.Visible : Visibility.Collapsed;

	internal Visibility SettingsVisibility => Quota.RequiresElevation ? Visibility.Collapsed : Visibility.Visible;

	internal QuotaPropertyView(WindowsShellQuotaProperties quota, HWND owner, Action<string> showError)
	{
		Quota = quota;
		_owner = owner;
		_showError = showError;
		InitializeComponent();
	}

	private static string FormatQuotaBytes(long value)
	{
		if (value < 0)
		{
			return Strings.NoLimit.GetLocalized();
		}

		var gigabytes = value / 1_073_741_824d;

		return string.Format(CultureInfo.CurrentCulture, "{0:N2} {1}", gigabytes, Strings.GigabyteSymbol.GetLocalized());
	}

	private void ShowQuotaSettings_Click(object sender, RoutedEventArgs e)
	{
		var result = WindowsShellQuotaService.ShowSettings(_owner, Quota.RootPath, Quota.DisplayName);
		if (result.Failed)
		{
			_showError(new System.Runtime.InteropServices.COMException(null, result.Value).Message);
		}
	}
}
