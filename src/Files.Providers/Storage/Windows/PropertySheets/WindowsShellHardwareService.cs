// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.Foundation;

namespace Files.Core.Storage.Windows;

/// <summary>Opens the system property editor for a hardware device.</summary>
public static class WindowsShellHardwareService
{
	/// <summary>Shows device properties using the caller's window as a borrowed owner.</summary>
	/// <param name="ownerWindowHandle">The borrowed owner window handle.</param>
	/// <param name="deviceInstanceId">The device instance identifier.</param>
	/// <returns>The Win32 error code, or zero on success.</returns>
	public static int ShowProperties(nint ownerWindowHandle, string deviceInstanceId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);

		return PInvoke.DevicePropertiesEx((HWND)ownerWindowHandle, null, deviceInstanceId, 0, false);
	}
}
