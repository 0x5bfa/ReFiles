// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Windows.Win32;

public static partial class PInvoke
{
	[LibraryImport("Windows.Storage.dll", EntryPoint = "CItemStore_CreateInstance")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	internal static partial HRESULT CItemStoreCreateInstance([MarshalAs(UnmanagedType.Interface)] object? outer, in Guid interfaceId, out IDefViewItemStore itemStore);
}
