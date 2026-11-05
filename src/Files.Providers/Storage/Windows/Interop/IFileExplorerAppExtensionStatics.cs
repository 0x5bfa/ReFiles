// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT;

/// <summary>Enumerates Explorer app extensions for an item type.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("104C1AFF-F09F-5AA1-945F-78737EE0FE45")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtensionStatics : IInspectable
{
	/// <summary>Gets extensions registered for an item type.</summary>
	/// <param name="extension">The item type.</param>
	/// <param name="flags">The cache lookup flags.</param>
	/// <param name="extensions">Receives a vector view of extensions.</param>
	/// <returns>The HRESULT returned by the extension cache.</returns>
	[PreserveSig]
	HRESULT GetExtensions([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string extension, uint flags, [MarshalAs(UnmanagedType.Interface)] out object? extensions);

	/// <summary>Gets every cached Explorer app extension.</summary>
	/// <param name="flags">The cache lookup flags.</param>
	/// <param name="extensions">Receives a vector view of extensions.</param>
	/// <returns>The HRESULT returned by the extension cache.</returns>
	[PreserveSig]
	HRESULT GetAllExtensions(uint flags, [MarshalAs(UnmanagedType.Interface)] out object? extensions);
}
