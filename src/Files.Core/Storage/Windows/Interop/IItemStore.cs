// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

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

/// <summary>Provides Explorer's recovered item-store contract.</summary>
[GeneratedComInterface, Guid("DF44CD5D-9AE9-4935-980A-E8ADD2246D41"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IItemStore
{
	/// <summary>Determines whether the store contains an item.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT ContainsItem(in ITEMKEY itemKey);

	/// <summary>Gets an item's child identifier.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="childId">Receives the child identifier.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT GetItemIDList(in ITEMKEY itemKey, out IChildId? childId);

	/// <summary>Gets an item's state flags.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="flags">Receives the item flags.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT GetItemFlags(in ITEMKEY itemKey, out ITEM_FLAGS flags);

	/// <summary>Counts items matching a flag mask.</summary>
	/// <param name="mask">The flags to test.</param>
	/// <param name="value">The required flag values.</param>
	/// <param name="count">Receives the matching item count.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT GetItemCount(ITEM_FLAGS mask, ITEM_FLAGS value, out int count);

	/// <summary>Finds an item's store key from its parent folder and child PIDL.</summary>
	/// <param name="parentFolder">The parent Shell folder.</param>
	/// <param name="childPidl">The child PIDL.</param>
	/// <param name="itemKey">Receives the item key.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT FindItemKey(IShellFolder parentFolder, in ITEMIDLIST childPidl, out ITEMKEY itemKey);

	/// <summary>Gets an item through the requested interface.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="parentFolder">The parent Shell folder.</param>
	/// <param name="viewStateInfo">Optional view-state information.</param>
	/// <param name="interfaceId">The requested interface identifier.</param>
	/// <param name="item">Receives the requested interface.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT GetItem(in ITEMKEY itemKey, IShellFolder parentFolder, [MarshalAs(UnmanagedType.Interface)] object? viewStateInfo, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object? item);

	/// <summary>Gets an array of items matching the supplied flags.</summary>
	/// <param name="parentFolder">The parent Shell folder.</param>
	/// <param name="options">The item retrieval options.</param>
	/// <param name="mask">The flags to test.</param>
	/// <param name="value">The required flag values.</param>
	/// <param name="interfaceId">The requested interface identifier.</param>
	/// <param name="items">Receives the requested item array.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT GetItemArray(IShellFolder parentFolder, ITEMSTORE_GETITEMFLAGS options, ITEM_FLAGS mask, ITEM_FLAGS value, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object? items);

	/// <summary>Gets an item's property store.</summary>
	/// <param name="storeType">The property-store type.</param>
	/// <param name="itemKey">The item key.</param>
	/// <param name="parentFolder">The parent Shell folder.</param>
	/// <param name="viewStateInfo">Optional view-state information.</param>
	/// <param name="create">Whether the store may be created.</param>
	/// <param name="interfaceId">The requested interface identifier.</param>
	/// <param name="propertyStore">Receives the requested property store.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT GetItemPropertyStore(GET_PROPERTY_STORE_TYPE storeType, in ITEMKEY itemKey, IShellFolder2 parentFolder, [MarshalAs(UnmanagedType.Interface)] object? viewStateInfo, BOOL create,
		in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object? propertyStore);

	/// <summary>Resets an item's update flags.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="flags">The flags to reset.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT ResetItemUpdateFlags(in ITEMKEY itemKey, ITEM_FLAGS flags);

	/// <summary>Marks an item as failing realization.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT SetFailedToRealizeItem(in ITEMKEY itemKey);

	/// <summary>Inserts an item into the store.</summary>
	/// <param name="childId">The child identifier.</param>
	/// <param name="flags">The initial item flags.</param>
	/// <param name="propertyStore">Optional initial properties.</param>
	/// <param name="itemKey">Receives the inserted item key.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT InsertItem(IChildId childId, ITEM_FLAGS flags, IPropertyStore? propertyStore, out ITEMKEY itemKey);

	/// <summary>Sets an item's category value.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="category">The category property key.</param>
	/// <param name="value">The category value.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT SetItemCategory(in ITEMKEY itemKey, in PROPERTYKEY category, in PROPVARIANT value);

	/// <summary>Gets an item's category value.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="category">The category property key.</param>
	/// <param name="value">Receives the category value.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT GetItemCategory(in ITEMKEY itemKey, in PROPERTYKEY category, out PROPVARIANT value);

	/// <summary>Gets an unrealized item through the requested interface.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="interfaceId">The requested interface identifier.</param>
	/// <param name="item">Receives the requested interface.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT GetUnrealizedItem(in ITEMKEY itemKey, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object? item);
}
