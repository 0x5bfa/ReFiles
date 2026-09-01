// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace Windows.Win32;

public static partial class PInvoke
{
	[LibraryImport("ntshrui.dll", EntryPoint = "CanShareFolder", StringMarshalling = StringMarshalling.Utf16)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	internal static partial HRESULT CanShareFolder(string path);

	[LibraryImport("ntshrui.dll", EntryPoint = "ShowShareFolderUI", StringMarshalling = StringMarshalling.Utf16)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	internal static partial HRESULT ShowShareFolderUI(HWND owner, string path);
}
