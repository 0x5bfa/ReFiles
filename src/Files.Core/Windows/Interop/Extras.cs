// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.Urlmon;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.IO;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Windows.Win32
{
	public static partial class PInvoke
	{
		/// <summary>Specifies the 32-bit ARGB pixel format used by Windows imaging APIs.</summary>
		public const int PixelFormat32bppARGB = 2498570;

		[LibraryImport("ole32.dll", EntryPoint = "CoGetObject", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT CoGetObject(string displayName, in BIND_OPTS3 bindOptions, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object? instance);

		/// <summary>Evaluates URL security policy through the Shell URL zone helper exported by ordinal 233.</summary>
		[LibraryImport("shlwapi.dll", EntryPoint = "#233", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT ZoneCheckUrlExCache(
			string url, out uint policy, uint policySize, nint context, uint contextSize, uint action, uint flags, IInternetSecurityMgrSite? securitySite, nint securityManagerCache);

		/// <summary>Reads a file's URL zone from its alternate data stream.</summary>
		[LibraryImport("urlmon.dll", EntryPoint = "GetZoneFromAlternateDataStreamEx", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT GetZoneFromAlternateDataStreamEx(string filePath, out uint zone);

		[LibraryImport("Windows.Storage.dll", EntryPoint = "GetCachedIniForFolder")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT GetCachedIniForFolder(uint reserved, in ITEMIDLIST pidl, uint flags, out ICachedPrivateProfile? cachedProfile);

		[LibraryImport("Windows.Storage.dll", EntryPoint = "IsPathOwnedByCurrentUser", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial int IsPathOwnedByCurrentUser(string path);

		[LibraryImport("shlwapi.dll", EntryPoint = "#626", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT SHCreatePropertyBagOnCachedProfileSection(ICachedPrivateProfile cachedProfile, string section, uint mode, in Guid interfaceId, out IPropertyBag? propertyBag);

		[LibraryImport("shlwapi.dll", EntryPoint = "SHGetViewStatePropertyBag", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT SHGetViewStatePropertyBag(in ITEMIDLIST pidl, string bagName, uint flags, in Guid interfaceId, out IPropertyBag? propertyBag);

		[LibraryImport("propsys.dll", EntryPoint = "PSGetPropertyDescriptionListFromString", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT PSGetPropertyDescriptionListFromString(string propertyList, in Guid interfaceId, out IPropertyDescriptionList? descriptions);

		[LibraryImport("Windows.Storage.dll", EntryPoint = "CItemStore_CreateInstance")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT CItemStoreCreateInstance([MarshalAs(UnmanagedType.Interface)] object? outer, in Guid interfaceId, out IDefViewItemStore? itemStore);

		[LibraryImport("ntshrui.dll", EntryPoint = "CanShareFolder", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT CanShareFolder(string path);

		[LibraryImport("ntshrui.dll", EntryPoint = "ShowShareFolderUI", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT ShowShareFolderUI(HWND owner, string path);

		[LibraryImport("ext-ms-win-storage-sense-l1-1-0.dll", EntryPoint = "GetStorageInstanceCount")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT GetStorageInstanceCount(uint category, out uint count);

		[LibraryImport("ext-ms-win-storage-sense-l1-1-0.dll", EntryPoint = "GetStorageDeviceInfo")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT GetStorageDeviceInfo(uint category, uint index, Span<byte> information);

		/// <summary>Refreshes the Recycle Bin icon.</summary>
		[LibraryImport("shell32.dll", EntryPoint = "SHUpdateRecycleBinIcon", SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		public static partial void SHUpdateRecycleBinIcon();

		/// <summary>Opens the system device-properties UI for a device instance.</summary>
		/// <param name="parent">The owner window.</param>
		/// <param name="machineName">The optional remote machine name.</param>
		/// <param name="deviceInstanceId">The device instance identifier.</param>
		/// <param name="flags">Reserved flags.</param>
		/// <param name="showDeviceTree">Whether to show the Device Manager tree.</param>
		/// <returns>The native result code.</returns>
		[LibraryImport("devmgr.dll", EntryPoint = "DevicePropertiesExW", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		public static partial int DevicePropertiesEx(HWND parent, string? machineName, string deviceInstanceId, uint flags, BOOL showDeviceTree);

		[LibraryImport("ntdll.dll", EntryPoint = "NtFsControlFile")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial NTSTATUS NtFsControlFile(SafeFileHandle fileHandle, SafeFileHandle eventHandle, nint apcRoutine, nint apcContext, ref IO_STATUS_BLOCK ioStatusBlock, uint controlCode,
			nint inputBuffer, uint inputBufferLength, ref byte outputBuffer, uint outputBufferLength);
	}
}
