// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT;

/// <summary>Provides mutable access to a string-to-inspectable map.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("1B0D3570-0877-5EC2-8A2C-3B9539506ACA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IStringInspectableMap : IInspectable
{
	/// <summary>Gets a value by key.</summary>
	/// <param name="key">The lookup key.</param>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT Lookup([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key, out IInspectable? value);

	/// <summary>Gets the number of entries.</summary>
	/// <param name="size">Receives the entry count.</param>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT GetSize(out uint size);

	/// <summary>Determines whether the map contains a key.</summary>
	/// <param name="key">The lookup key.</param>
	/// <param name="found">Receives whether the key exists.</param>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT HasKey([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key, [MarshalAs(UnmanagedType.U1)] out bool found);

	/// <summary>Gets a read-only view of the map.</summary>
	/// <param name="view">Receives the map view.</param>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT GetView(out IStringInspectableMapView? view);

	/// <summary>Inserts or replaces a value.</summary>
	/// <param name="key">The key to insert.</param>
	/// <param name="value">The value to insert.</param>
	/// <param name="replaced">Receives whether an existing value was replaced.</param>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT Insert([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key, IInspectable? value, [MarshalAs(UnmanagedType.U1)] out bool replaced);

	/// <summary>Removes a value.</summary>
	/// <param name="key">The key to remove.</param>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT Remove([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key);

	/// <summary>Removes every value.</summary>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT Clear();
}
