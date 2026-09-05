// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT;

/// <summary>Provides indexed access to Explorer verb property sets.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("A79C3578-C5C8-5B61-AFC7-8DA842914434")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPropertySetVectorView : IInspectable
{
	/// <summary>Gets a property set by index.</summary>
	/// <param name="index">The zero-based index.</param>
	/// <param name="value">Receives the property set.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetAt(uint index, out IPropertySet? value);

	/// <summary>Gets the number of property sets.</summary>
	/// <param name="size">Receives the number of property sets.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetSize(out uint size);

	/// <summary>Finds a property set in the vector.</summary>
	/// <param name="value">The property set to find.</param>
	/// <param name="index">Receives its index.</param>
	/// <param name="found">Receives whether the property set was found.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT IndexOf(IPropertySet? value, out uint index, [MarshalAs(UnmanagedType.U1)] out bool found);

	/// <summary>Copies property sets into a caller-provided array.</summary>
	/// <param name="startIndex">The first index to copy.</param>
	/// <param name="capacity">The array capacity.</param>
	/// <param name="values">The destination array.</param>
	/// <param name="actual">Receives the number copied.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetMany(uint startIndex, uint capacity, [Out, MarshalUsing(CountElementName = nameof(capacity))] IPropertySet?[] values, out uint actual);
}
