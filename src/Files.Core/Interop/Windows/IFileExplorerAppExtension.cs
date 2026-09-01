// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT;

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper), Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IWinRtInspectable
{
	[PreserveSig]
	HRESULT GetIids(out uint iidCount, out global::Windows.Win32.ComHeapPtr<Guid> iids);

	[PreserveSig]
	HRESULT GetRuntimeClassName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? className);

	[PreserveSig]
	HRESULT GetTrustLevel(out int trustLevel);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper), Guid("104C1AFF-F09F-5AA1-945F-78737EE0FE45"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtensionStatics : IWinRtInspectable
{
	[PreserveSig]
	HRESULT GetExtensions([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string extension, uint flags,
		[MarshalUsing(typeof(UniqueComInterfaceMarshaller<IWinRtInspectable>))] out IWinRtInspectable? extensions);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper), Guid("66C23D78-8E71-51EF-A184-ED7B6E6CDC90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtension8972 : IWinRtInspectable
{
	[PreserveSig]
	HRESULT GetDisplayName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? displayName);

	[PreserveSig]
	HRESULT GetPackageFullName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? packageFullName);

	[PreserveSig]
	HRESULT GetLogo([MarshalUsing(typeof(UniqueComInterfaceMarshaller<IWinRtInspectable>))] out IWinRtInspectable? logo);

	[PreserveSig]
	HRESULT GetVerbs(uint flags, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IFileExplorerValueSetVectorView>))] out IFileExplorerValueSetVectorView? verbs);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper), Guid("EB16945B-3A5D-5A8A-B666-8DFE27E5C818"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtension9278 : IWinRtInspectable
{
	[PreserveSig]
	HRESULT GetDisplayName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? displayName);

	[PreserveSig]
	HRESULT GetPackageFullName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? packageFullName);

	[PreserveSig]
	HRESULT GetLogo([MarshalUsing(typeof(UniqueComInterfaceMarshaller<IWinRtInspectable>))] out IWinRtInspectable? logo);

	[PreserveSig]
	HRESULT GetIsL1Default(out byte isDefault);

	[PreserveSig]
	HRESULT GetVerbs(uint flags, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IFileExplorerValueSetVectorView>))] out IFileExplorerValueSetVectorView? verbs);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper), Guid("08ED2B5C-6EE2-5C20-B0E1-A1C0C739F17E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtensionVectorView8972 : IWinRtInspectable
{
	[PreserveSig]
	HRESULT GetAt(uint index, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IWinRtInspectable>))] out IWinRtInspectable? extension);

	[PreserveSig]
	HRESULT GetSize(out uint size);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper), Guid("0E5F24ED-9BC4-5B7B-9DDB-46150318F868"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerAppExtensionVectorView9278 : IWinRtInspectable
{
	[PreserveSig]
	HRESULT GetAt(uint index, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IWinRtInspectable>))] out IWinRtInspectable? extension);

	[PreserveSig]
	HRESULT GetSize(out uint size);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper), Guid("A79C3578-C5C8-5B61-AFC7-8DA842914434"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IFileExplorerValueSetVectorView : IWinRtInspectable
{
	[PreserveSig]
	HRESULT GetAt(uint index, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IWinRtPropertySet>))] out IWinRtPropertySet? valueSet);

	[PreserveSig]
	HRESULT GetSize(out uint size);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper), Guid("8A43ED9F-F4E6-4421-ACF9-1DAB2986820C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IWinRtPropertySet : IWinRtInspectable
{
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper), Guid("1B0D3570-0877-5EC2-8A2C-3B9539506ACA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IWinRtStringInspectableMap : IWinRtInspectable
{
	[PreserveSig]
	HRESULT Lookup([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string key,
		[MarshalUsing(typeof(UniqueComInterfaceMarshaller<IWinRtInspectable>))] out IWinRtInspectable? value);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper), Guid("4BD682DD-7554-40E9-9A9B-82654EDE7E62"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IWinRtPropertyValue : IWinRtInspectable
{
	[PreserveSig]
	HRESULT GetPropertyType(out int value);

	[PreserveSig]
	HRESULT GetIsNumericScalar(out byte value);

	[PreserveSig]
	HRESULT GetUInt8(out byte value);

	[PreserveSig]
	HRESULT GetInt16(out short value);

	[PreserveSig]
	HRESULT GetUInt16(out ushort value);

	[PreserveSig]
	HRESULT GetInt32(out int value);

	[PreserveSig]
	HRESULT GetUInt32(out uint value);

	[PreserveSig]
	HRESULT GetInt64(out long value);

	[PreserveSig]
	HRESULT GetUInt64(out ulong value);

	[PreserveSig]
	HRESULT GetSingle(out float value);

	[PreserveSig]
	HRESULT GetDouble(out double value);

	[PreserveSig]
	HRESULT GetChar16(out ushort value);

	[PreserveSig]
	HRESULT GetBoolean(out byte value);

	[PreserveSig]
	HRESULT GetString([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? value);

	[PreserveSig]
	HRESULT GetGuid(out Guid value);
}
