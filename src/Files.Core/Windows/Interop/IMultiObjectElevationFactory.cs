// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.UI.Shell;

/// <summary>Identifies the Shell multi-object elevation factory coclass.</summary>
[Guid("36F0BD14-D84D-468C-B79C-9990F3FA897F")]
internal sealed class CMultiObjectElevationFactory
{
}

/// <summary>Creates an elevated sharing object after associating it with an owner window.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("6FABDA16-031E-47E3-B2A2-2339C05CCB9E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IMultiObjectElevationFactory
{
	/// <summary>Initializes the factory for an elevated coclass.</summary>
	/// <param name="owner">The owner window.</param>
	/// <param name="classId">The elevated coclass identifier.</param>
	/// <returns>The HRESULT returned by the factory.</returns>
	[PreserveSig]
	HRESULT Initialize(HWND owner, in Guid classId);

	/// <summary>Initializes the factory without changing the wait cursor.</summary>
	/// <param name="owner">The owner window.</param>
	/// <param name="classId">The elevated coclass identifier.</param>
	/// <returns>The HRESULT returned by the factory.</returns>
	[PreserveSig]
	HRESULT InitializeNoWaitCursor(HWND owner, in Guid classId);

	/// <summary>Creates the requested elevated object.</summary>
	/// <param name="classId">The coclass identifier.</param>
	/// <param name="interfaceId">The requested interface identifier.</param>
	/// <param name="instance">Receives the requested interface.</param>
	/// <returns>The HRESULT returned by the factory.</returns>
	[PreserveSig]
	HRESULT CreateElevatedObject(in Guid classId, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object? instance);
}
