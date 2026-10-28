// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT;

/// <summary>Provides read-only access to a string-to-inspectable map.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("BB78502A-F79D-54FA-92C9-90C5039FDF7E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IStringInspectableMapView : IInspectable
{
	/// <summary>Gets a value by key.</summary>
	/// <param name="key">The lookup key.</param>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the map view.</returns>
	[PreserveSig]
	HRESULT Lookup([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key, out IInspectable? value);

	/// <summary>Gets the number of entries.</summary>
	/// <param name="size">Receives the entry count.</param>
	/// <returns>The HRESULT returned by the map view.</returns>
	[PreserveSig]
	HRESULT GetSize(out uint size);

	/// <summary>Determines whether the map view contains a key.</summary>
	/// <param name="key">The lookup key.</param>
	/// <param name="found">Receives whether the key exists.</param>
	/// <returns>The HRESULT returned by the map view.</returns>
	[PreserveSig]
	HRESULT HasKey([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key, [MarshalAs(UnmanagedType.U1)] out bool found);

	/// <summary>Splits the map view into two views.</summary>
	/// <param name="first">Receives the first view.</param>
	/// <param name="second">Receives the second view.</param>
	/// <returns>The HRESULT returned by the map view.</returns>
	[PreserveSig]
	HRESULT Split(out IStringInspectableMapView? first, out IStringInspectableMapView? second);
}
