// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.UI.Shell;

/// <summary>Identifies the Shell NTFS security extension coclass.</summary>
[Guid("1F2E5C40-9550-11CE-99D2-00AA006E086C")]
internal sealed class CNtfsSecurityExtension
{
}

/// <summary>Opens the elevated NTFS security editor.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("74807F67-0058-440D-8600-65541A7FBBEA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IElevatedNtfsSecurity
{
	/// <summary>Opens a security editor page for a filesystem object.</summary>
	/// <param name="owner">The owning window.</param>
	/// <param name="resourceName">The filesystem resource name.</param>
	/// <param name="objectName">The filesystem object name.</param>
	/// <param name="isDirectory">Whether the object is a directory.</param>
	/// <param name="page">The security editor page.</param>
	/// <returns>The HRESULT returned by the security extension.</returns>
	[PreserveSig]
	HRESULT OpenEditor(HWND owner, [MarshalAs(UnmanagedType.BStr)] string resourceName, [MarshalAs(UnmanagedType.BStr)] string objectName, uint isDirectory, uint page);
}
