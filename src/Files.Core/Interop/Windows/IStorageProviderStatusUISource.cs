// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using WinRT;

namespace Windows.Win32.System.WinRT;

/// <summary>Provides access to a storage provider status user interface.</summary>
[GeneratedComInterface, Guid("A306C249-3D66-5E70-9007-E43DF96051FF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IStorageProviderStatusUISource
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

	/// <summary>Gets the current status user interface.</summary>
	/// <param name="result">Receives the status interface.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetStatusUI([MarshalAs(UnmanagedType.Interface)] out IStorageProviderStatusUI result);

	/// <summary>Registers a handler for status interface changes.</summary>
	/// <param name="handler">The event handler pointer.</param>
	/// <param name="token">Receives the registration token.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT AddStatusUIChanged(
		/* TypedEventHandler<IStorageProviderStatusUISource*, IInspectable*>* */ nint handler,
		out EventRegistrationToken token);

	/// <summary>Removes a previously registered status change handler.</summary>
	/// <param name="token">The registration token.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT RemoveStatusUIChanged(EventRegistrationToken token);
}
