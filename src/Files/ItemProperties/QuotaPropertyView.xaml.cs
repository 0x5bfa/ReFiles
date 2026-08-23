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

	internal string DenyDiskSpaceLabel => Strings.DenyDiskSpace.GetLocalized();

	internal string ElevationDescription => Strings.QuotaElevationDescription.GetLocalized();

	internal Visibility ElevationVisibility => Quota.RequiresElevation ? Visibility.Visible : Visibility.Collapsed;

	internal string EnableQuotaManagementLabel => Strings.EnableQuotaManagement.GetLocalized();

	internal string LimitDiskSpaceLabel => Strings.LimitDiskSpaceTo.GetLocalized();

	internal string LogLimitEventLabel => Strings.LogLimitEvent.GetLocalized();

	internal string LogWarningEventLabel => Strings.LogWarningEvent.GetLocalized();

	internal string QuotaEntriesLabel => Strings.QuotaEntries.GetLocalized();

	internal string QuotaLoggingLabel => Strings.QuotaLogging.GetLocalized();

	internal string QuotaManagementDescription => Strings.QuotaManagementDescription.GetLocalized();

	internal string QuotaManagementLabel => Strings.QuotaManagement.GetLocalized();

	internal Visibility SettingsVisibility => Quota.RequiresElevation ? Visibility.Collapsed : Visibility.Visible;

	internal string ShowQuotaSettingsLabel => Strings.ShowQuotaSettings.GetLocalized();

	internal string WarningLevelLabel => Strings.SetWarningLevelTo.GetLocalized();

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
