// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT;

/// <summary>Provides the base Windows Runtime inspection contract.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IInspectable
{
	/// <summary>Gets the interface identifiers implemented by the object.</summary>
	/// <param name="iidCount">Receives the number of identifiers.</param>
	/// <param name="iids">Receives the allocated identifier array.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetIids(out uint iidCount, out global::Windows.Win32.ComHeapPtr<Guid> iids);

	/// <summary>Gets the runtime class name.</summary>
	/// <param name="className">Receives the runtime class name.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetRuntimeClassName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? className);

	/// <summary>Gets the runtime trust level.</summary>
	/// <param name="trustLevel">Receives the trust level.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetTrustLevel(out TrustLevel trustLevel);
}
