// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.Foundation;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Opens the elevated Windows NTFS disk-quota editor used by the Shell property page.
/// </summary>
public static unsafe class WindowsShellQuotaService
{
	private static readonly Guid _interfaceId = new("9A50588E-FA80-4509-B345-664110225322");

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

		void* helper = null;
		var result = WindowsElevationMoniker.Create(owner, CLSID.CLSID_QuotaUIHelper, _interfaceId, &helper);
		if (result.Failed || helper is null)
		{
			Release(helper);

			return result.Failed ? result : HRESULT.E_FAIL;
		}

		try
		{
			fixed (char* rootPathPointer = rootPath)
			fixed (char* displayNamePointer = displayName)
			{
				var showSettings = (delegate* unmanaged[Stdcall]<void*, nint, char*, char*, char*, HRESULT>)(*(void***)helper)[3];

				return showSettings(helper, (nint)owner.Value, rootPathPointer, displayNamePointer, rootPathPointer);
			}
		}
		finally
		{
			Release(helper);
		}
	}

	private static void Release(void* instance)
	{
		if (instance is not null)
		{
			((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)instance)[2])(instance);
		}
	}
}
