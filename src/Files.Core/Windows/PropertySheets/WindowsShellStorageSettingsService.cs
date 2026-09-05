// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

/// <summary>
/// Opens the Windows Settings storage page used by Explorer's drive property sheet.
/// </summary>
public static class WindowsShellStorageSettingsService
{
	private const int StorageDeviceInformationSize = 1112;
	private const int StorageDeviceRootOffset = 4;
	private const int StorageDeviceStateOffset = 528;
	private const int StorageDeviceRootCapacity = 262;
	private const string SettingsApplicationId = "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";
	private const string StorageVolumeArguments = "page=SettingsPageStorageSenseStorageOverview&target=SystemSettings_StorageSense_VolumeListLink"
		+ "&l3target=SystemSettings_StorageSense_VolumeInfoList&selectpath=";

	/// <summary>
	/// Opens the storage-usage details for a drive.
	/// </summary>
	/// <param name="rootPath">The drive root to select.</param>
	/// <returns>The HRESULT returned by application activation.</returns>
	public static HRESULT OpenDriveUsage(string rootPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

		try
		{
			var activationManager = ApplicationActivationManager.CreateInstance<IApplicationActivationManager>();

			return activationManager.ActivateApplication(SettingsApplicationId, StorageVolumeArguments + rootPath, ACTIVATEOPTIONS.AO_NONE, out _);
		}
		catch (COMException exception)
		{
			return (HRESULT)exception.HResult;
		}
	}

	/// <summary>
	/// Determines whether Explorer exposes storage-usage details for a drive.
	/// </summary>
	/// <param name="rootPath">The drive root to inspect.</param>
	/// <returns><see langword="true"/> when the drive is present in the Storage Sense inventory.</returns>
	public static bool SupportsDriveUsage(string rootPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

		var root = Path.GetPathRoot(rootPath);
		if (string.IsNullOrWhiteSpace(root))
		{
			return false;
		}

		try
		{
			Span<byte> information = stackalloc byte[StorageDeviceInformationSize];
			HRESULT hr;
			for (uint category = 0; category < 2; category++)
			{
				hr = PInvoke.GetStorageInstanceCount(category, out var count);
				if (hr.Failed)
				{
					continue;
				}

				for (uint index = 0; index < count; index++)
				{
					information.Clear();
					var informationSize = StorageDeviceInformationSize;
					MemoryMarshal.Write(information, in informationSize);
					hr = PInvoke.GetStorageDeviceInfo(category, index, information);
					if (hr.Failed || MemoryMarshal.Read<int>(information[StorageDeviceStateOffset..]) is not 0)
					{
						continue;
					}

					var enumeratedRoot = new string(MemoryMarshal.Cast<byte, char>(information[StorageDeviceRootOffset..])[..StorageDeviceRootCapacity]).TrimEnd('\0');
					if (root.Equals(enumeratedRoot, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
			}
		}
		catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
		{
		}

		return false;
	}
}
