// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT;

/// <summary>Creates status user interface sources for storage providers.</summary>
[GeneratedComInterface, Guid("12E46B74-4E5A-58D1-A62F-0376E8EE7DD8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IStorageProviderStatusUISourceFactory
{
	/// <summary>Gets the interfaces implemented by the object.</summary>
	/// <param name="iidCount">Receives the number of interface identifiers.</param>
	/// <param name="iids">Receives the interface identifier array.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetIids(out uint iidCount, out /* IID** */ nint iids);

	/// <summary>Gets the WinRT runtime class name.</summary>
	/// <param name="className">Receives the runtime class name.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetRuntimeClassName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? className);

	/// <summary>Gets the WinRT trust level.</summary>
	/// <param name="trustLevel">Receives the trust level.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetTrustLevel(out TrustLevel trustLevel);

	/// <summary>Gets a status user interface source for a sync root.</summary>
	/// <param name="syncRootId">The sync root identifier.</param>
	/// <param name="result">Receives the status source.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetStatusUISource([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string? syncRootId, [MarshalAs(UnmanagedType.Interface)] out IStorageProviderStatusUISource result);
}
