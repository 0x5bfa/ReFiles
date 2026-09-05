// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.UI.Shell;

/// <summary>Identifies the Shell disk-quota UI helper coclass.</summary>
[Guid("1FB2A002-4C6C-4DE7-85C2-CB8DB9A4F728")]
internal sealed class CQuotaUIHelper
{
}

/// <summary>Opens the elevated disk quota user interface.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("9A50588E-FA80-4509-B345-664110225322")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IElevatedDiskQuotaUI
{
	/// <summary>Opens the quota settings for a volume.</summary>
	/// <param name="owner">The owning window.</param>
	/// <param name="rootPath">The volume root path.</param>
	/// <param name="displayName">The Shell display name for the volume.</param>
	/// <param name="volumePath">The path identifying the volume.</param>
	/// <returns>The HRESULT returned by the quota user interface.</returns>
	[PreserveSig]
	HRESULT ShowVolumeQuotaUI(HWND owner, string rootPath, string displayName, string volumePath);
}
