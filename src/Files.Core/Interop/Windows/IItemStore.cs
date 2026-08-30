// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Windows.Win32.UI.Shell;

[StructLayout(LayoutKind.Sequential)]
internal struct ITEMKEY
{
	internal int Index;

	internal uint Generation;
}

[Flags]
internal enum ITEM_FLAGS : uint
{
	None = 0,
	Valid = 1,
}

internal enum ITEMSTORE_GETITEMFLAGS : uint
{
	None = 0,
}

internal enum GET_PROPERTY_STORE_TYPE : uint
{
	Default = 0,
}

[GeneratedComInterface, Guid("C412BF5B-91EA-4904-B34A-855504FBBF0B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IChildId
{
}

[GeneratedComInterface, Guid("DF44CD5D-9AE9-4935-980A-E8ADD2246D41"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe partial interface IItemStore
{
	[PreserveSig]
	HRESULT ContainsItem(in ITEMKEY itemKey);

	[PreserveSig]
	HRESULT GetItemIDList(in ITEMKEY itemKey, out IChildId childId);

	[PreserveSig]
	HRESULT GetItemFlags(in ITEMKEY itemKey, out ITEM_FLAGS flags);

	[PreserveSig]
	HRESULT GetItemCount(ITEM_FLAGS mask, ITEM_FLAGS value, out int count);

	[PreserveSig]
	HRESULT FindItemKey(IShellFolder parentFolder, ITEMIDLIST* childPidl, out ITEMKEY itemKey);

	[PreserveSig]
	HRESULT GetItem(in ITEMKEY itemKey, IShellFolder parentFolder, [MarshalAs(UnmanagedType.Interface)] object? viewStateInfo, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object item);

	[PreserveSig]
	HRESULT GetItemArray(IShellFolder parentFolder, ITEMSTORE_GETITEMFLAGS options, ITEM_FLAGS mask, ITEM_FLAGS value, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object items);

	[PreserveSig]
	HRESULT GetItemPropertyStore(GET_PROPERTY_STORE_TYPE storeType, in ITEMKEY itemKey, IShellFolder2 parentFolder, [MarshalAs(UnmanagedType.Interface)] object? viewStateInfo, BOOL create, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object propertyStore);

	[PreserveSig]
	HRESULT ResetItemUpdateFlags(in ITEMKEY itemKey, ITEM_FLAGS flags);

	[PreserveSig]
	HRESULT SetFailedToRealizeItem(in ITEMKEY itemKey);

	[PreserveSig]
	HRESULT InsertItem(IChildId childId, ITEM_FLAGS flags, IPropertyStore? propertyStore, out ITEMKEY itemKey);

	[PreserveSig]
	HRESULT SetItemCategory(in ITEMKEY itemKey, in PROPERTYKEY category, in PROPVARIANT value);

	[PreserveSig]
	HRESULT GetItemCategory(in ITEMKEY itemKey, in PROPERTYKEY category, out PROPVARIANT value);

	[PreserveSig]
	HRESULT GetUnrealizedItem(in ITEMKEY itemKey, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object item);
}

[GeneratedComInterface, Guid("35C22FEA-79F6-4D3D-AE15-1699CBF54721"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe partial interface IDefViewItemStore : IItemStore
{
	[PreserveSig]
	HRESULT Initialize(ITEMIDLIST* rootPidl);
}
