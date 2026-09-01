// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Windows.Win32;

public static partial class PInvoke
{
	[LibraryImport("propsys.dll", EntryPoint = "PSGetPropertyDescriptionListFromString", StringMarshalling = StringMarshalling.Utf16)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	internal static partial HRESULT PSGetPropertyDescriptionListFromString(string propertyList, in Guid interfaceId,
		[MarshalUsing(typeof(UniqueComInterfaceMarshaller<IPropertyDescriptionList>))] out IPropertyDescriptionList descriptions);
}
