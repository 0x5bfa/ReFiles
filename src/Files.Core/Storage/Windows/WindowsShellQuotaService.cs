// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Opens the elevated Windows NTFS disk-quota editor used by the Shell property page.
/// </summary>
public static class WindowsShellQuotaService
{
	/// <summary>
	/// Opens the quota editor for a volume through the Shell elevation helper.
	/// </summary>
	/// <param name="owner">The window that owns the editor and elevation prompt.</param>
	/// <param name="rootPath">The volume root path.</param>
	/// <param name="displayName">The Shell display name for the volume.</param>
	/// <returns>The HRESULT returned by the quota UI helper.</returns>
	public static HRESULT ShowSettings(HWND owner, string rootPath, string displayName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

		var result = WindowsElevationMoniker.Create<IElevatedDiskQuotaUI>(owner, CLSID.CLSID_QuotaUIHelper, out var helper);
		if (result.Failed || helper is null)
		{
			ReleaseComObject(helper);

			return result.Failed ? result : HRESULT.E_FAIL;
		}

		try
		{
			return helper.ShowVolumeQuotaUI(owner, rootPath, displayName, rootPath);
		}
		finally
		{
			ReleaseComObject(helper);
		}
	}

	private static void ReleaseComObject(object? instance)
	{
		if (instance is ComObject comObject)
		{
			comObject.FinalRelease();
		}
	}
}
