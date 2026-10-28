// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Windows.Win32.System.WinRT;

/// <summary>Identifies a Windows Runtime property set. This interface adds no ABI slots; map access is obtained through <see cref="IStringInspectableMap"/>.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("8A43ED9F-F4E6-4421-ACF9-1DAB2986820C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPropertySet : IInspectable
{
}
