// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.UI.Shell;

/// <summary>Identifies the Shell mount-point rename coclass.</summary>
[Guid("60173D16-A550-47F0-A14B-C6F9E4DA0831")]
internal sealed class CMountPointRename
{
}

/// <summary>Renames a filesystem mount point through the Shell's elevated service.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("92F8D886-AB61-4113-BD4F-2E894397386F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IMountPointRename
{
	/// <summary>Changes a volume label.</summary>
	/// <param name="rootPath">The volume root path.</param>
	/// <param name="label">The new volume label.</param>
	/// <returns>The HRESULT returned by the Shell service.</returns>
	[PreserveSig]
	HRESULT Rename(string rootPath, string label);
}
