// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT
{
	/// <summary>Represents the quota information exposed by a storage provider.</summary>
	[GeneratedComInterface, Guid("BA6295C3-312E-544F-9FD5-1F81B21F3649"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public partial interface IStorageProviderQuotaUI
	{
		/// <summary>Gets the interfaces implemented by the object.</summary>
		/// <param name="iidCount">Receives the number of interface identifiers.</param>
		/// <param name="iids">Receives the interface identifier array.</param>
		/// <returns>The HRESULT returned by the object.</returns>
		[PreserveSig]
		HRESULT GetIids(out uint iidCount, out nint iids);

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

		/// <summary>Gets the total quota in bytes.</summary>
		/// <param name="value">Receives the quota.</param>
		/// <returns>The HRESULT returned by the object.</returns>
		[PreserveSig]
		HRESULT GetQuotaTotalInBytes(out ulong value);

		/// <summary>Sets the total quota in bytes.</summary>
		/// <param name="value">The quota to expose.</param>
		/// <returns>The HRESULT returned by the object.</returns>
		[PreserveSig]
		HRESULT PutQuotaTotalInBytes(ulong value);

		/// <summary>Gets the quota currently used in bytes.</summary>
		/// <param name="value">Receives the used quota.</param>
		/// <returns>The HRESULT returned by the object.</returns>
		[PreserveSig]
		HRESULT GetQuotaUsedInBytes(out ulong value);

		/// <summary>Sets the quota currently used in bytes.</summary>
		/// <param name="value">The used quota to expose.</param>
		/// <returns>The HRESULT returned by the object.</returns>
		[PreserveSig]
		HRESULT PutQuotaUsedInBytes(ulong value);

		/// <summary>Gets the localized label for used quota.</summary>
		/// <param name="value">Receives the label.</param>
		/// <returns>The HRESULT returned by the object.</returns>
		[PreserveSig]
		HRESULT GetQuotaUsedLabel([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? value);

		/// <summary>Sets the localized label for used quota.</summary>
		/// <param name="value">The label to expose.</param>
		/// <returns>The HRESULT returned by the object.</returns>
		[PreserveSig]
		HRESULT PutQuotaUsedLabel([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string? value);

		/// <summary>Gets the color used to display quota usage.</summary>
		/// <param name="value">Receives the color value.</param>
		/// <returns>The HRESULT returned by the object.</returns>
		[PreserveSig]
		HRESULT GetQuotaUsedColor(out nint value);

		/// <summary>Sets the color used to display quota usage.</summary>
		/// <param name="value">The color value to expose.</param>
		/// <returns>The HRESULT returned by the object.</returns>
		[PreserveSig]
		HRESULT PutQuotaUsedColor(nint value);
	}
}
