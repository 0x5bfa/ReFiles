// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.IO;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32
{
	public static partial class PInvoke
	{
		/// <summary>Specifies the 32-bit ARGB pixel format used by Windows imaging APIs.</summary>
		public const int PixelFormat32bppARGB = 2498570;

		[LibraryImport("Windows.Storage.dll", EntryPoint = "GetCachedIniForFolder")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static unsafe partial HRESULT GetCachedIniForFolder(uint reserved, ITEMIDLIST* pidl, uint flags,
			[MarshalUsing(typeof(UniqueComInterfaceMarshaller<ICachedIniUnknown>))] out ICachedIniUnknown cachedProfile);

		[LibraryImport("Windows.Storage.dll", EntryPoint = "IsPathOwnedByCurrentUser", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial int IsPathOwnedByCurrentUser(string path);

		[LibraryImport("shlwapi.dll", EntryPoint = "#626", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT SHCreatePropertyBagOnCachedProfileSection([MarshalUsing(typeof(ComInterfaceMarshaller<ICachedPrivateProfile>))] ICachedPrivateProfile cachedProfile,
			string section, uint mode, in Guid interfaceId, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IPropertyBag>))] out IPropertyBag propertyBag);

		[LibraryImport("shlwapi.dll", EntryPoint = "SHGetViewStatePropertyBag", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static unsafe partial HRESULT SHGetViewStatePropertyBag(ITEMIDLIST* pidl, string bagName, uint flags, in Guid interfaceId,
			[MarshalUsing(typeof(UniqueComInterfaceMarshaller<IPropertyBag>))] out IPropertyBag propertyBag);

		[LibraryImport("ext-ms-win-storage-sense-l1-1-0.dll", EntryPoint = "GetStorageInstanceCount")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static unsafe partial HRESULT GetStorageInstanceCount(uint category, uint* count);

		[LibraryImport("ext-ms-win-storage-sense-l1-1-0.dll", EntryPoint = "GetStorageDeviceInfo")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static unsafe partial HRESULT GetStorageDeviceInfo(uint category, uint index, void* information);

		/// <summary>Sets a window long value using the pointer-sized Windows API.</summary>
		/// <param name="hWnd">The target window handle.</param>
		/// <param name="nIndex">The value index.</param>
		/// <param name="dwNewLong">The new value.</param>
		/// <returns>The previous value.</returns>
		public static nint SetWindowLongPtr(HWND hWnd, WINDOW_LONG_PTR_INDEX nIndex, nint dwNewLong)
		{
			return _SetWindowLongPtr(hWnd, (int)nIndex, dwNewLong);
		}

		/// <summary>Refreshes the Recycle Bin icon.</summary>
		[LibraryImport("shell32.dll", EntryPoint = "SHUpdateRecycleBinIcon", SetLastError = true)]
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
		internal static unsafe partial int NtFsControlFile(
			SafeHandle fileHandle,
			SafeHandle eventHandle,
			nint apcRoutine,
			nint apcContext,
			IO_STATUS_BLOCK* ioStatusBlock,
			uint controlCode,
			void* inputBuffer,
			uint inputBufferLength,
			void* outputBuffer,
			uint outputBufferLength);

		[LibraryImport("User32", EntryPoint = "SetWindowLongPtrW")]
		private static partial nint _SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
	}

	namespace Extras
	{
		/// <summary>Delegate used to enumerate monitors through the Win32 API.</summary>
		/// <param name="param0">The monitor handle.</param>
		/// <param name="param1">The device context handle.</param>
		/// <param name="param2">The monitor rectangle.</param>
		/// <param name="param3">The application-defined value.</param>
		/// <returns>A nonzero value to continue enumeration.</returns>
		[UnmanagedFunctionPointer(CallingConvention.Winapi)]
		public unsafe delegate BOOL ManagedMONITORENUMPROC([In] HMONITOR param0, [In] HDC param1, [In][Out] RECT* param2, [In] LPARAM param3);

		/// <summary>Delegate used as a managed window procedure callback.</summary>
		/// <param name="hWnd">The window handle.</param>
		/// <param name="msg">The window message.</param>
		/// <param name="wParam">The message parameter.</param>
		/// <param name="lParam">The message parameter.</param>
		/// <returns>The result of processing the message.</returns>
		[UnmanagedFunctionPointer(CallingConvention.Winapi)]
		public delegate LRESULT ManagedWNDPROC(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam);

		/// <summary>Represents a DirectComposition target.</summary>
		[GeneratedComInterface, Guid("EACDD04C-117E-4E17-88F4-D1B12B0E3D89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		public partial interface IDCompositionTarget
		{
			/// <summary>Sets the root visual for the target.</summary>
			/// <param name="visual">The visual to use as the root.</param>
			/// <returns>The HRESULT returned by DirectComposition.</returns>
			[PreserveSig]
			int SetRoot(IDCompositionVisual visual);
		}
	}
}
