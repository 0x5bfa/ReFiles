// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.UI.Shell;

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("92F8D886-AB61-4113-BD4F-2E894397386F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IMountPointRename
{
	[PreserveSig]
	HRESULT Rename(string rootPath, string label);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper), Guid("6FABDA16-031E-47E3-B2A2-2339C05CCB9E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IMultiObjectElevationFactory
{
	[PreserveSig]
	HRESULT Initialize(HWND owner, in Guid classId);

	[PreserveSig]
	HRESULT InitializeNoWaitCursor(HWND owner, in Guid classId);

	[PreserveSig]
	HRESULT CreateElevatedObject(in Guid classId, in Guid interfaceId, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<ISharingConfigurationUI>))] out ISharingConfigurationUI? instance);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("14AA4AB8-ABE3-4A07-A290-1D5DCCDD2FC2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface ISharingConfigurationUI
{
	[PreserveSig]
	HRESULT CanShareItems([MarshalAs(UnmanagedType.Interface)] IShellItemArray items);

	[PreserveSig]
	HRESULT ShowShareItemsUI(HWND owner, [MarshalAs(UnmanagedType.Interface)] IShellItemArray items);

	[PreserveSig]
	HRESULT ShowShareItemsUIMakePrivate(HWND owner, [MarshalAs(UnmanagedType.Interface)] IShellItemArray items);

	[PreserveSig]
	HRESULT ShowShareItemsUIShareToHomeGroup(HWND owner, [MarshalAs(UnmanagedType.Interface)] IShellItemArray items, BOOL share);

	[PreserveSig]
	HRESULT ShowShareItemsUIStopSharingToHomeGroup(HWND owner, [MarshalAs(UnmanagedType.Interface)] IShellItemArray items, BOOL share);

	[PreserveSig]
	HRESULT ShowShareItemsUIShareToUser(HWND owner, [MarshalAs(UnmanagedType.Interface)] IShellItemArray items, string userName, BOOL applyPermissions);

	[PreserveSig]
	HRESULT ShowShareItemsUIApplyPermissions(HWND owner, [MarshalAs(UnmanagedType.Interface)] IShellItemArray sourceItems,
		[MarshalAs(UnmanagedType.Interface)] IShellItemArray targetItems, int action);

	[PreserveSig]
	HRESULT ShowAdvancedSharingConfigDialog(HWND owner, string path);
}
