// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.UI.Shell;

/// <summary>Opens Windows Control Panel pages.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("D11AD862-66DE-4DF4-BF6C-1F5621996AF1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IOpenControlPanel
{
	/// <summary>Opens a Control Panel page.</summary>
	/// <param name="name">The page name.</param>
	/// <param name="page">The optional page identifier.</param>
	/// <param name="site">The optional site.</param>
	/// <returns>The HRESULT returned by Control Panel.</returns>
	[PreserveSig]
	HRESULT Open(string name, string? page, [MarshalAs(UnmanagedType.Interface)] object? site);
}
