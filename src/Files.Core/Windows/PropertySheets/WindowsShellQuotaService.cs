// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

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

		var classId = typeof(CQuotaUIHelper).GUID;
		var interfaceId = typeof(IElevatedDiskQuotaUI).GUID;
		BIND_OPTS3 bindOptions = default;
		bindOptions.Base.Base.cbStruct = checked((uint)Marshal.SizeOf<BIND_OPTS3>());
		bindOptions.Base.dwClassContext = (uint)CLSCTX.CLSCTX_LOCAL_SERVER;
		bindOptions.hwnd = owner;
		var hr = PInvoke.CoGetObject($"Elevation:Administrator!new:{classId:B}", in bindOptions, in interfaceId, out object helperObject);
		var helper = helperObject as IElevatedDiskQuotaUI;
		if (hr.Failed || helper is null)
		{
			return hr.Failed ? hr : HRESULT.E_NOINTERFACE;
		}

		hr = helper.ShowVolumeQuotaUI(owner, rootPath, displayName, rootPath);

		return hr;
	}
}
