// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT;

/// <summary>Provides the base Windows Runtime inspection contract.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IInspectable
{
	/// <summary>Gets the interface identifiers implemented by the object.</summary>
	/// <param name="iidCount">Receives the number of identifiers.</param>
	/// <param name="iids">Receives the allocated identifier array.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetIids(out uint iidCount, out global::Windows.Win32.ComHeapPtr<Guid> iids);

	/// <summary>Gets the runtime class name.</summary>
	/// <param name="className">Receives the runtime class name.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetRuntimeClassName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? className);

	/// <summary>Gets the runtime trust level.</summary>
	/// <param name="trustLevel">Receives the trust level.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetTrustLevel(out TrustLevel trustLevel);
}

/// <summary>Enumerates Explorer app extensions for an item type.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("104C1AFF-F09F-5AA1-945F-78737EE0FE45")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtensionStatics : IInspectable
{
	/// <summary>Gets extensions registered for an item type.</summary>
	/// <param name="extension">The item type.</param>
	/// <param name="flags">The cache lookup flags.</param>
	/// <param name="extensions">Receives a vector view of extensions.</param>
	/// <returns>The HRESULT returned by the extension cache.</returns>
	[PreserveSig]
	HRESULT GetExtensions([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string extension, uint flags, [MarshalAs(UnmanagedType.Interface)] out object? extensions);

	/// <summary>Gets every cached Explorer app extension.</summary>
	/// <param name="flags">The cache lookup flags.</param>
	/// <param name="extensions">Receives a vector view of extensions.</param>
	/// <returns>The HRESULT returned by the extension cache.</returns>
	[PreserveSig]
	HRESULT GetAllExtensions(uint flags, [MarshalAs(UnmanagedType.Interface)] out object? extensions);
}

/// <summary>Describes the original recovered Explorer app-extension ABI.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("66C23D78-8E71-51EF-A184-ED7B6E6CDC90")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtension : IInspectable
{
	/// <summary>Gets the extension display name.</summary>
	/// <param name="displayName">Receives the display name.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetDisplayName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? displayName);

	/// <summary>Gets the package full name.</summary>
	/// <param name="packageFullName">Receives the package full name.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetPackageFullName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? packageFullName);

	/// <summary>Gets the extension logo.</summary>
	/// <param name="logo">Receives the logo object.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetLogo([MarshalAs(UnmanagedType.Interface)] out object? logo);

	/// <summary>Gets the extension verbs.</summary>
	/// <param name="flags">The cache lookup flags.</param>
	/// <param name="verbs">Receives the verb vector.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetVerbs(uint flags, out IPropertySetVectorView? verbs);

	/// <summary>Gets the extended verb collection.</summary>
	/// <param name="flags">The cache lookup flags.</param>
	/// <param name="verbs">Receives the extended verb object.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetVerbsEx(uint flags, [MarshalAs(UnmanagedType.Interface)] out object? verbs);

	/// <summary>Gets the extension conditions.</summary>
	/// <param name="conditions">Receives the conditions object.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetConditions([MarshalAs(UnmanagedType.Interface)] out object? conditions);
}

/// <summary>Describes the second recovered Explorer app-extension ABI, which adds the level-one default state.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("EB16945B-3A5D-5A8A-B666-8DFE27E5C818")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtension2 : IInspectable
{
	/// <summary>Gets the extension display name.</summary>
	/// <param name="displayName">Receives the display name.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetDisplayName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? displayName);

	/// <summary>Gets the package full name.</summary>
	/// <param name="packageFullName">Receives the package full name.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetPackageFullName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? packageFullName);

	/// <summary>Gets the extension logo.</summary>
	/// <param name="logo">Receives the logo object.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetLogo([MarshalAs(UnmanagedType.Interface)] out object? logo);

	/// <summary>Gets whether the extension is the level-one default.</summary>
	/// <param name="isDefault">Receives the default state.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetIsL1Default([MarshalAs(UnmanagedType.U1)] out bool isDefault);

	/// <summary>Gets the extension verbs.</summary>
	/// <param name="flags">The cache lookup flags.</param>
	/// <param name="verbs">Receives the verb vector.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetVerbs(uint flags, out IPropertySetVectorView? verbs);

	/// <summary>Gets the extended verb collection.</summary>
	/// <param name="flags">The cache lookup flags.</param>
	/// <param name="verbs">Receives the extended verb object.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetVerbsEx(uint flags, [MarshalAs(UnmanagedType.Interface)] out object? verbs);

	/// <summary>Gets the extension conditions.</summary>
	/// <param name="conditions">Receives the conditions object.</param>
	/// <returns>The HRESULT returned by the extension.</returns>
	[PreserveSig]
	HRESULT GetConditions([MarshalAs(UnmanagedType.Interface)] out object? conditions);
}

/// <summary>Provides indexed access to the original app-extension interface.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("08ED2B5C-6EE2-5C20-B0E1-A1C0C739F17E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtensionVectorView : IInspectable
{
	/// <summary>Gets an extension by index.</summary>
	/// <param name="index">The zero-based index.</param>
	/// <param name="extension">Receives the extension.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetAt(uint index, out IFileExplorerAppExtension? extension);

	/// <summary>Gets the number of extensions.</summary>
	/// <param name="size">Receives the number of extensions.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetSize(out uint size);

	/// <summary>Finds an extension in the vector.</summary>
	/// <param name="value">The extension to find.</param>
	/// <param name="index">Receives its index.</param>
	/// <param name="found">Receives whether the extension was found.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT IndexOf(IFileExplorerAppExtension? value, out uint index, [MarshalAs(UnmanagedType.U1)] out bool found);

	/// <summary>Copies extensions into a caller-provided array.</summary>
	/// <param name="startIndex">The first index to copy.</param>
	/// <param name="capacity">The array capacity.</param>
	/// <param name="values">The destination array.</param>
	/// <param name="actual">Receives the number copied.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetMany(uint startIndex, uint capacity, [Out, MarshalUsing(CountElementName = nameof(capacity))] IFileExplorerAppExtension?[] values, out uint actual);
}

/// <summary>Provides indexed access to the second app-extension interface revision.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("0E5F24ED-9BC4-5B7B-9DDB-46150318F868")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtensionVectorView2 : IInspectable
{
	/// <summary>Gets an extension by index.</summary>
	/// <param name="index">The zero-based index.</param>
	/// <param name="extension">Receives the extension.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetAt(uint index, out IFileExplorerAppExtension2? extension);

	/// <summary>Gets the number of extensions.</summary>
	/// <param name="size">Receives the number of extensions.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetSize(out uint size);

	/// <summary>Finds an extension in the vector.</summary>
	/// <param name="value">The extension to find.</param>
	/// <param name="index">Receives its index.</param>
	/// <param name="found">Receives whether the extension was found.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT IndexOf(IFileExplorerAppExtension2? value, out uint index, [MarshalAs(UnmanagedType.U1)] out bool found);

	/// <summary>Copies extensions into a caller-provided array.</summary>
	/// <param name="startIndex">The first index to copy.</param>
	/// <param name="capacity">The array capacity.</param>
	/// <param name="values">The destination array.</param>
	/// <param name="actual">Receives the number copied.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetMany(uint startIndex, uint capacity, [Out, MarshalUsing(CountElementName = nameof(capacity))] IFileExplorerAppExtension2?[] values, out uint actual);
}

/// <summary>Provides indexed access to Explorer verb property sets.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("A79C3578-C5C8-5B61-AFC7-8DA842914434")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPropertySetVectorView : IInspectable
{
	/// <summary>Gets a property set by index.</summary>
	/// <param name="index">The zero-based index.</param>
	/// <param name="value">Receives the property set.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetAt(uint index, out IPropertySet? value);

	/// <summary>Gets the number of property sets.</summary>
	/// <param name="size">Receives the number of property sets.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetSize(out uint size);

	/// <summary>Finds a property set in the vector.</summary>
	/// <param name="value">The property set to find.</param>
	/// <param name="index">Receives its index.</param>
	/// <param name="found">Receives whether the property set was found.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT IndexOf(IPropertySet? value, out uint index, [MarshalAs(UnmanagedType.U1)] out bool found);

	/// <summary>Copies property sets into a caller-provided array.</summary>
	/// <param name="startIndex">The first index to copy.</param>
	/// <param name="capacity">The array capacity.</param>
	/// <param name="values">The destination array.</param>
	/// <param name="actual">Receives the number copied.</param>
	/// <returns>The HRESULT returned by the vector.</returns>
	[PreserveSig]
	HRESULT GetMany(uint startIndex, uint capacity, [Out, MarshalUsing(CountElementName = nameof(capacity))] IPropertySet?[] values, out uint actual);
}

/// <summary>Identifies a Windows Runtime property set. This interface adds no ABI slots; map access is obtained through <see cref="IStringInspectableMap"/>.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("8A43ED9F-F4E6-4421-ACF9-1DAB2986820C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPropertySet : IInspectable
{
}

/// <summary>Provides mutable access to a string-to-inspectable map.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("1B0D3570-0877-5EC2-8A2C-3B9539506ACA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IStringInspectableMap : IInspectable
{
	/// <summary>Gets a value by key.</summary>
	/// <param name="key">The lookup key.</param>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT Lookup([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key, out IInspectable? value);

	/// <summary>Gets the number of entries.</summary>
	/// <param name="size">Receives the entry count.</param>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT GetSize(out uint size);

	/// <summary>Determines whether the map contains a key.</summary>
	/// <param name="key">The lookup key.</param>
	/// <param name="found">Receives whether the key exists.</param>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT HasKey([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key, [MarshalAs(UnmanagedType.U1)] out bool found);

	/// <summary>Gets a read-only view of the map.</summary>
	/// <param name="view">Receives the map view.</param>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT GetView(out IStringInspectableMapView? view);

	/// <summary>Inserts or replaces a value.</summary>
	/// <param name="key">The key to insert.</param>
	/// <param name="value">The value to insert.</param>
	/// <param name="replaced">Receives whether an existing value was replaced.</param>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT Insert([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key, IInspectable? value, [MarshalAs(UnmanagedType.U1)] out bool replaced);

	/// <summary>Removes a value.</summary>
	/// <param name="key">The key to remove.</param>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT Remove([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key);

	/// <summary>Removes every value.</summary>
	/// <returns>The HRESULT returned by the map.</returns>
	[PreserveSig]
	HRESULT Clear();
}

/// <summary>Provides read-only access to a string-to-inspectable map.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("BB78502A-F79D-54FA-92C9-90C5039FDF7E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IStringInspectableMapView : IInspectable
{
	/// <summary>Gets a value by key.</summary>
	/// <param name="key">The lookup key.</param>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the map view.</returns>
	[PreserveSig]
	HRESULT Lookup([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key, out IInspectable? value);

	/// <summary>Gets the number of entries.</summary>
	/// <param name="size">Receives the entry count.</param>
	/// <returns>The HRESULT returned by the map view.</returns>
	[PreserveSig]
	HRESULT GetSize(out uint size);

	/// <summary>Determines whether the map view contains a key.</summary>
	/// <param name="key">The lookup key.</param>
	/// <param name="found">Receives whether the key exists.</param>
	/// <returns>The HRESULT returned by the map view.</returns>
	[PreserveSig]
	HRESULT HasKey([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key, [MarshalAs(UnmanagedType.U1)] out bool found);

	/// <summary>Splits the map view into two views.</summary>
	/// <param name="first">Receives the first view.</param>
	/// <param name="second">Receives the second view.</param>
	/// <returns>The HRESULT returned by the map view.</returns>
	[PreserveSig]
	HRESULT Split(out IStringInspectableMapView? first, out IStringInspectableMapView? second);
}

/// <summary>Reads scalar and array values from a Windows Runtime property value.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("4BD682DD-7554-40E9-9A9B-82654EDE7E62")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPropertyValue : IInspectable
{
	/// <summary>Gets the stored property type.</summary>
	/// <param name="value">Receives the property type.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetPropertyType(out global::Windows.Foundation.PropertyType value);

	/// <summary>Gets whether the value is a numeric scalar.</summary>
	/// <param name="value">Receives whether the value is a numeric scalar.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetIsNumericScalar([MarshalAs(UnmanagedType.U1)] out bool value);

	/// <summary>Gets an unsigned 8-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt8(out byte value);

	/// <summary>Gets a signed 16-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInt16(out short value);

	/// <summary>Gets an unsigned 16-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt16(out ushort value);

	/// <summary>Gets a signed 32-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInt32(out int value);

	/// <summary>Gets an unsigned 32-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt32(out uint value);

	/// <summary>Gets a signed 64-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInt64(out long value);

	/// <summary>Gets an unsigned 64-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt64(out ulong value);

	/// <summary>Gets a single-precision value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetSingle(out float value);

	/// <summary>Gets a double-precision value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetDouble(out double value);

	/// <summary>Gets a UTF-16 character.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetChar16(out char value);

	/// <summary>Gets a Boolean value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetBoolean([MarshalAs(UnmanagedType.U1)] out bool value);

	/// <summary>Gets a string value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetString([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? value);

	/// <summary>Gets a GUID value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetGuid(out Guid value);

	/// <summary>Gets a Windows Runtime date-time ABI value.</summary>
	/// <param name="universalTime">Receives the universal-time tick value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetDateTime(out long universalTime);

	/// <summary>Gets a Windows Runtime time-span ABI value.</summary>
	/// <param name="duration">Receives the duration tick value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetTimeSpan(out long duration);

	/// <summary>Gets a point value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetPoint(out global::Windows.Foundation.Point value);

	/// <summary>Gets a size value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetSize(out global::Windows.Foundation.Size value);

	/// <summary>Gets a rectangle value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetRect(out global::Windows.Foundation.Rect value);

	/// <summary>Gets an unsigned 8-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt8Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out byte[]? value);

	/// <summary>Gets a signed 16-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInt16Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out short[]? value);

	/// <summary>Gets an unsigned 16-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt16Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out ushort[]? value);

	/// <summary>Gets a signed 32-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInt32Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out int[]? value);

	/// <summary>Gets an unsigned 32-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt32Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out uint[]? value);

	/// <summary>Gets a signed 64-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInt64Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out long[]? value);

	/// <summary>Gets an unsigned 64-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt64Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out ulong[]? value);

	/// <summary>Gets a single-precision array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetSingleArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out float[]? value);

	/// <summary>Gets a double-precision array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetDoubleArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out double[]? value);

	/// <summary>Gets a UTF-16 character array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetChar16Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out ushort[]? value);

	/// <summary>Gets a Windows Runtime Boolean array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated one-byte Boolean values.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetBooleanArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out byte[]? value);

	/// <summary>Gets a string array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetStringArray(out uint valueLength,
		[MarshalUsing(CountElementName = nameof(valueLength))]
		[MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller), ElementIndirectionDepth = 1)] out string?[]? value);

	/// <summary>Gets an inspectable array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInspectableArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out IInspectable?[]? value);

	/// <summary>Gets a GUID array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetGuidArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out Guid[]? value);

	/// <summary>Gets a Windows Runtime date-time ABI array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated universal-time tick values.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetDateTimeArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out long[]? value);

	/// <summary>Gets a Windows Runtime time-span ABI array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated duration tick values.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetTimeSpanArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out long[]? value);

	/// <summary>Gets a point array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetPointArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out global::Windows.Foundation.Point[]? value);

	/// <summary>Gets a size array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetSizeArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out global::Windows.Foundation.Size[]? value);

	/// <summary>Gets a rectangle array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetRectArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out global::Windows.Foundation.Rect[]? value);
}
