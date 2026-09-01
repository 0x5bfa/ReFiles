// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace Files.Core.Interop.Windows;

[SupportedOSPlatform("windows6.0.6000")]
internal static partial class PreviewHandlerNativeMethods
{
	internal static HRESULT SHCreatePreviewStream(string fileSystemPath, uint mode, out IStream stream)
	{
		return SHCreateStreamOnFileEx(fileSystemPath, mode, 0, false, null, out stream);
	}

	internal static HRESULT SHCreatePreviewItem(string parsingName, out IShellItem item)
	{
		var interfaceId = typeof(IShellItem).GUID;

		return SHCreateItemFromParsingName(parsingName, null, in interfaceId, out item);
	}

	[LibraryImport("shlwapi.dll", EntryPoint = "SHCreateStreamOnFileEx", StringMarshalling = StringMarshalling.Utf16)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial HRESULT SHCreateStreamOnFileEx(string fileSystemPath, uint mode, uint attributes, BOOL create,
		[MarshalUsing(typeof(ComInterfaceMarshaller<IStream>))] IStream? streamTemplate, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IStream>))] out IStream stream);

	[LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", StringMarshalling = StringMarshalling.Utf16)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial HRESULT SHCreateItemFromParsingName(string parsingName, [MarshalUsing(typeof(ComInterfaceMarshaller<IBindCtx>))] IBindCtx? bindContext, in Guid interfaceId,
		[MarshalUsing(typeof(UniqueComInterfaceMarshaller<IShellItem>))] out IShellItem item);
}
