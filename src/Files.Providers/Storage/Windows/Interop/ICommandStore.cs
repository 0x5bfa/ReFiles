// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.UI.Shell;

/// <summary>Resolves canonical Shell commands through the Windows command store.</summary>
/// <remarks>The first method is verified in CCommandStore and ContextMenuPresenter::GetCommandStoreCommand.</remarks>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("3EF82A5E-F7D2-4688-9E64-90614E7820C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface ICommandStore
{
	/// <summary>Gets the requested interface for a canonical command.</summary>
	/// <param name="canonicalName">The canonical command-store name.</param>
	/// <param name="interfaceId">The requested Explorer command interface identifier.</param>
	/// <param name="command">Receives the command.</param>
	/// <returns>The HRESULT returned by the command store.</returns>
	[PreserveSig]
	HRESULT GetCanonicalCommand(string canonicalName, in Guid interfaceId, out IExplorerCommand? command);
}

/// <summary>Identifies the command-store coclass implemented by Windows.Storage.dll.</summary>
[Guid("F5A8B627-4D46-4D65-92F3-73626AE31971")]
internal sealed class CommandStoreCoclass
{
}
