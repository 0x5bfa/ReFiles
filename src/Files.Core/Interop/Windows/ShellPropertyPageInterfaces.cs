// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.UI.Shell;

/// <summary>Identifies the Shell mount-point rename coclass.</summary>
[Guid("60173D16-A550-47F0-A14B-C6F9E4DA0831")]
internal sealed class CMountPointRename
{
}

/// <summary>Identifies the Shell multi-object elevation factory coclass.</summary>
[Guid("36F0BD14-D84D-468C-B79C-9990F3FA897F")]
internal sealed class CMultiObjectElevationFactory
{
}

/// <summary>Identifies the Shell elevated sharing factory coclass.</summary>
[Guid("72A7994A-3092-4054-B6BE-08FF81AEEFFC")]
internal sealed class CSharingElevatedFactory
{
}

/// <summary>Renames a filesystem mount point through the Shell's elevated service.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("92F8D886-AB61-4113-BD4F-2E894397386F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IMountPointRename
{
	/// <summary>Changes a volume label.</summary>
	/// <param name="rootPath">The volume root path.</param>
	/// <param name="label">The new volume label.</param>
	/// <returns>The HRESULT returned by the Shell service.</returns>
	[PreserveSig]
	HRESULT Rename(string rootPath, string label);
}

/// <summary>Creates an elevated sharing object after associating it with an owner window.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("6FABDA16-031E-47E3-B2A2-2339C05CCB9E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IMultiObjectElevationFactory
{
	/// <summary>Initializes the factory for an elevated coclass.</summary>
	/// <param name="owner">The owner window.</param>
	/// <param name="classId">The elevated coclass identifier.</param>
	/// <returns>The HRESULT returned by the factory.</returns>
	[PreserveSig]
	HRESULT Initialize(HWND owner, in Guid classId);

	/// <summary>Initializes the factory without changing the wait cursor.</summary>
	/// <param name="owner">The owner window.</param>
	/// <param name="classId">The elevated coclass identifier.</param>
	/// <returns>The HRESULT returned by the factory.</returns>
	[PreserveSig]
	HRESULT InitializeNoWaitCursor(HWND owner, in Guid classId);

	/// <summary>Creates the requested elevated object.</summary>
	/// <param name="classId">The coclass identifier.</param>
	/// <param name="interfaceId">The requested interface identifier.</param>
	/// <param name="instance">Receives the requested interface.</param>
	/// <returns>The HRESULT returned by the factory.</returns>
	[PreserveSig]
	HRESULT CreateElevatedObject(in Guid classId, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object? instance);
}

/// <summary>Runs the Shell's sharing commands for a collection of items.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("14AA4AB8-ABE3-4A07-A290-1D5DCCDD2FC2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface ISharingConfigurationUI
{
	/// <summary>Determines whether the selected items can be shared.</summary>
	/// <param name="items">The selected items.</param>
	/// <returns>The HRESULT returned by the sharing provider.</returns>
	[PreserveSig]
	HRESULT CanShareItems(IShellItemArray items);

	/// <summary>Shows the sharing UI for the selected items.</summary>
	/// <param name="owner">The owner window.</param>
	/// <param name="items">The selected items.</param>
	/// <returns>The HRESULT returned by the sharing provider.</returns>
	[PreserveSig]
	HRESULT ShowShareItemsUI(HWND owner, IShellItemArray items);

	/// <summary>Shows the UI that makes the selected items private.</summary>
	/// <param name="owner">The owner window.</param>
	/// <param name="items">The selected items.</param>
	/// <returns>The HRESULT returned by the sharing provider.</returns>
	[PreserveSig]
	HRESULT ShowShareItemsUIMakePrivate(HWND owner, IShellItemArray items);

	/// <summary>Shows the UI that changes HomeGroup sharing for the selected items.</summary>
	/// <param name="owner">The owner window.</param>
	/// <param name="items">The selected items.</param>
	/// <param name="share">Whether to enable sharing.</param>
	/// <returns>The HRESULT returned by the sharing provider.</returns>
	[PreserveSig]
	HRESULT ShowShareItemsUIShareToHomeGroup(HWND owner, IShellItemArray items, BOOL share);

	/// <summary>Shows the UI that stops HomeGroup sharing for the selected items.</summary>
	/// <param name="owner">The owner window.</param>
	/// <param name="items">The selected items.</param>
	/// <param name="share">The provider-specific sharing option.</param>
	/// <returns>The HRESULT returned by the sharing provider.</returns>
	[PreserveSig]
	HRESULT ShowShareItemsUIStopSharingToHomeGroup(HWND owner, IShellItemArray items, BOOL share);

	/// <summary>Shows the UI that shares the selected items with a user.</summary>
	/// <param name="owner">The owner window.</param>
	/// <param name="items">The selected items.</param>
	/// <param name="userName">The target user name.</param>
	/// <param name="applyPermissions">Whether to apply filesystem permissions.</param>
	/// <returns>The HRESULT returned by the sharing provider.</returns>
	[PreserveSig]
	HRESULT ShowShareItemsUIShareToUser(HWND owner, IShellItemArray items, string userName, BOOL applyPermissions);

	/// <summary>Applies sharing permissions from one item set to another.</summary>
	/// <param name="owner">The owner window.</param>
	/// <param name="sourceItems">The source items.</param>
	/// <param name="targetItems">The target items.</param>
	/// <param name="action">The provider-specific action.</param>
	/// <returns>The HRESULT returned by the sharing provider.</returns>
	[PreserveSig]
	HRESULT ShowShareItemsUIApplyPermissions(HWND owner, IShellItemArray sourceItems, IShellItemArray targetItems, int action);

	/// <summary>Shows the advanced sharing configuration dialog.</summary>
	/// <param name="owner">The owner window.</param>
	/// <param name="path">The folder path.</param>
	/// <returns>The HRESULT returned by the sharing provider.</returns>
	[PreserveSig]
	HRESULT ShowAdvancedSharingConfigDialog(HWND owner, string path);
}
