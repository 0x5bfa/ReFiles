// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT;

/// <summary>Describes the second recovered Explorer app-extension ABI, which adds the level-one default state.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("EB16945B-3A5D-5A8A-B666-8DFE27E5C818")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtension2 : IInspectable
{
	/// <summary>Gets the extension display name.</summary>
	/// <param name="displayName">Receives the display name.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetDisplayName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? displayName);

	/// <summary>Gets the package full name.</summary>
	/// <param name="packageFullName">Receives the package full name.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetPackageFullName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? packageFullName);

	/// <summary>Gets the extension logo.</summary>
	/// <param name="logo">Receives the logo object.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetLogo([MarshalAs(UnmanagedType.Interface)] out object? logo);

	/// <summary>Gets whether the extension is the level-one default.</summary>
	/// <param name="isDefault">Receives the default state.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetIsL1Default([MarshalAs(UnmanagedType.U1)] out bool isDefault);

	/// <summary>Gets the extension verbs.</summary>
	/// <param name="flags">The cache lookup flags.</param>
	/// <param name="verbs">Receives the verb vector.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetVerbs(uint flags, out IPropertySetVectorView? verbs);

	/// <summary>Gets the extended verb collection.</summary>
	/// <param name="flags">The cache lookup flags.</param>
	/// <param name="verbs">Receives the extended verb object.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetVerbsEx(uint flags, [MarshalAs(UnmanagedType.Interface)] out object? verbs);

	/// <summary>Gets the extension conditions.</summary>
	/// <param name="conditions">Receives the conditions object.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetConditions([MarshalAs(UnmanagedType.Interface)] out object? conditions);
}
