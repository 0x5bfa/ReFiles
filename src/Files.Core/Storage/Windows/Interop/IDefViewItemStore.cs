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

internal enum SET_PROPERTY_STORE_TYPE
{
	Default = 0,
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
internal struct ITEMGROUP
{
}

/// <summary>Adds the recovered DefView operations to Explorer's item-store contract.</summary>
[GeneratedComInterface, Guid("35C22FEA-79F6-4D3D-AE15-1699CBF54721"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe partial interface IDefViewItemStore : IItemStore
{
	/// <summary>Initializes the store for a root PIDL.</summary>
	/// <param name="rootPidl">The absolute root PIDL.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT Initialize(in ITEMIDLIST rootPidl);

	/// <summary>Deletes an item from the store.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT DeleteItem(in ITEMKEY itemKey);

	/// <summary>Sets an item's child identifier and selected flags.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="childId">The optional child identifier.</param>
	/// <param name="mask">The flags to change.</param>
	/// <param name="value">The new flag values.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT SetItemIDListAndFlags(in ITEMKEY itemKey, IChildId? childId, ITEM_FLAGS mask, ITEM_FLAGS value);

	/// <summary>Sets an item's property store.</summary>
	/// <param name="storeType">The property-store type.</param>
	/// <param name="itemKey">The item key.</param>
	/// <param name="propertyStore">The optional property store.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT SetItemPropertyStore(SET_PROPERTY_STORE_TYPE storeType, in ITEMKEY itemKey, IPropertyStore? propertyStore);

	/// <summary>Associates arbitrary COM data with an item.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="data">The optional data object, or <see langword="null"/> to clear it.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT SetData(in ITEMKEY itemKey, [MarshalAs(UnmanagedType.Interface)] object? data);

	/// <summary>Gets arbitrary COM data associated with an item.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="interfaceId">The requested interface identifier.</param>
	/// <param name="data">Receives the requested interface.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT GetData(in ITEMKEY itemKey, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object? data);

	/// <summary>Sets an item cookie.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="index">The cookie index.</param>
	/// <param name="cookie">The cookie value.</param>
	[PreserveSig]
	void SetItemCookie(in ITEMKEY itemKey, int index, uint cookie);

	/// <summary>Gets an item cookie.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="index">The cookie index.</param>
	/// <returns>The cookie value.</returns>
	[PreserveSig]
	uint GetItemCookie(in ITEMKEY itemKey, int index);

	/// <summary>Sets an item's opaque group records.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="groupType">The group type.</param>
	/// <param name="groups">The group records.</param>
	/// <param name="count">The number of group records.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT SetItemGroups(in ITEMKEY itemKey, uint groupType, [In, MarshalUsing(CountElementName = nameof(count))] ITEMGROUP[] groups, int count);

	/// <summary>Gets the store-owned group records for an item.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <param name="groupType">The group type.</param>
	/// <param name="groups">Receives a borrowed pointer to the group records. The caller must not free it.</param>
	/// <param name="count">Receives the number of group records.</param>
	/// <returns>The HRESULT returned by the store.</returns>
	[PreserveSig]
	HRESULT GetItemGroups(in ITEMKEY itemKey, uint groupType, out ITEMGROUP* groups, out int count);

	/// <summary>Determines whether an item key is valid.</summary>
	/// <param name="itemKey">The item key.</param>
	/// <returns><see langword="true"/> when the key is valid; otherwise, <see langword="false"/>.</returns>
	[PreserveSig]
	BOOL IsItemKeyValid(in ITEMKEY itemKey);
}
