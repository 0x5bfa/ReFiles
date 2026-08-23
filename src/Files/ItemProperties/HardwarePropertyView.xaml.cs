// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Files.Adapters;
using Files.Core.Storage.Windows;
using Files.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Files.ItemProperties;

public sealed partial class HardwarePropertyView : UserControl
{
	private readonly HWND _owner;
	private readonly Action<string> _showError;
	private WindowsShellHardwareDevice? _selectedDevice;

	internal ObservableCollection<HardwareDeviceViewModel> Devices { get; }

	internal string AllDiskDrivesLabel => Strings.AllDiskDrives.GetLocalized();

	internal string DeviceNameLabel => Strings.DeviceName.GetLocalized();

	internal string DeviceTypeLabel => Strings.DeviceType.GetLocalized();

	internal string LocationLabel => Strings.DeviceLocation.GetLocalized();

	internal string ManufacturerLabel => Strings.Manufacturer.GetLocalized();

	internal string PropertiesLabel => Strings.Properties.GetLocalized();

	internal string StatusLabel => Strings.DeviceStatus.GetLocalized();

	internal HardwarePropertyView(IReadOnlyList<WindowsShellHardwareDevice> devices, HWND owner, Action<string> showError)
	{
		Devices = new(devices.Select(static device => new HardwareDeviceViewModel(device)));
		_owner = owner;
		_showError = showError;
		InitializeComponent();
		if (Devices.Count is not 0)
		{
			HardwareList.SelectedIndex = 0;
		}

		_ = LoadDeviceIconsAsync();
	}

	private void HardwareList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		_selectedDevice = (HardwareList.SelectedItem as HardwareDeviceViewModel)?.Device;
		PropertiesButton.IsEnabled = _selectedDevice is not null;
		ManufacturerValue.Text = _selectedDevice?.Manufacturer ?? string.Empty;
		LocationValue.Text = FormatLocation(_selectedDevice);
		StatusValue.Text = _selectedDevice is null
			? string.Empty
			: _selectedDevice.ProblemCode is 0
				? Strings.DeviceWorkingProperly.GetLocalized()
				: string.Format(CultureInfo.CurrentCulture, Strings.DeviceProblemFormat.GetLocalized(), _selectedDevice.ProblemCode);
	}

	private void PropertiesButton_Click(object sender, RoutedEventArgs e)
	{
		if (_selectedDevice is null)
		{
			return;
		}

		var result = PInvoke.DevicePropertiesEx(_owner, null, _selectedDevice.InstanceId, 0, false);
		if (result is not 0)
		{
			_showError(new System.ComponentModel.Win32Exception(result).Message);
		}
	}

	private string FormatLocation(WindowsShellHardwareDevice? device)
	{
		if (device is null)
		{
			return string.Empty;
		}

		var number = device.LocationNumber is { } locationNumber ? FormatLocationNumber(device.LocationNumberFormat, locationNumber) : string.Empty;
		if (string.IsNullOrEmpty(number))
		{
			return string.IsNullOrEmpty(device.Location) ? Strings.Unspecified.GetLocalized() : device.Location;
		}

		return string.IsNullOrEmpty(device.Location) ? number : string.Format(CultureInfo.CurrentCulture, Strings.DeviceLocationDetailsFormat.GetLocalized(), number, device.Location);
	}

	private static string FormatLocationNumber(string format, uint locationNumber)
	{
		var number = locationNumber.ToString(CultureInfo.CurrentCulture);
		if (!string.IsNullOrEmpty(format))
		{
			var formatted = format.Replace("%1!u!", number, StringComparison.Ordinal).Replace("%1!d!", number, StringComparison.Ordinal).Replace("%1", number, StringComparison.Ordinal);
			if (!formatted.Equals(format, StringComparison.Ordinal))
			{
				return formatted;
			}
		}

		return string.Format(CultureInfo.CurrentCulture, Strings.DeviceLocationNumberFormat.GetLocalized(), locationNumber);
	}

	private async Task LoadDeviceIconsAsync()
	{
		foreach (var device in Devices)
		{
			await device.LoadIconAsync();
		}
	}
}

internal sealed class HardwareDeviceViewModel : INotifyPropertyChanged
{
	private BitmapImage? _icon;

	public BitmapImage? Icon
	{
		get => _icon;
		private set
		{
			if (!ReferenceEquals(_icon, value))
			{
				_icon = value;
				OnPropertyChanged();
			}
		}
	}

	public string Name => Device.Name;

	public string Type => Device.Type;

	internal WindowsShellHardwareDevice Device { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	internal HardwareDeviceViewModel(WindowsShellHardwareDevice device)
	{
		Device = device;
	}

	internal async Task LoadIconAsync()
	{
		if (Device.IconData.IsEmpty)
		{
			return;
		}

		Icon = await ThumbnailImageFactory.CreateAsync(Device.IconData);
	}

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
