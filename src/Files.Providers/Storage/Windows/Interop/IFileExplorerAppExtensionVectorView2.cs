// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT;

/// <summary>Provides indexed access to the second app-extension interface revision.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("0E5F24ED-9BC4-5B7B-9DDB-46150318F868")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtensionVectorView2 : IInspectable
{
	/// <summary>Gets an extension by index.</summary>
	/// <param name="index">The zero-based index.</param>
	/// <param name="extension">Receives the extension.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetAt(uint index, out IFileExplorerAppExtension2? extension);

	/// <summary>Gets the number of extensions.</summary>
	/// <param name="size">Receives the number of extensions.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetSize(out uint size);

	/// <summary>Finds an extension in the vector.</summary>
	/// <param name="value">The extension to find.</param>
	/// <param name="index">Receives its index.</param>
	/// <param name="found">Receives whether the extension was found.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT IndexOf(IFileExplorerAppExtension2? value, out uint index, [MarshalAs(UnmanagedType.U1)] out bool found);

	/// <summary>Copies extensions into a caller-provided array.</summary>
	/// <param name="startIndex">The first index to copy.</param>
	/// <param name="capacity">The array capacity.</param>
	/// <param name="values">The destination array.</param>
	/// <param name="actual">Receives the number copied.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetMany(uint startIndex, uint capacity, [Out, MarshalUsing(CountElementName = nameof(capacity))] IFileExplorerAppExtension2?[] values, out uint actual);
}
