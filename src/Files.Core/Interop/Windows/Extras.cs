// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32
{
	public static partial class PInvoke
	{
		/// <summary>Specifies the 32-bit ARGB pixel format used by Windows imaging APIs.</summary>
		public const int PixelFormat32bppARGB = 2498570;

		/// <summary>Creates a COM object using the raw ABI signature.</summary>
		/// <param name="classId">The class identifier.</param>
		/// <param name="outer">The controlling unknown, or zero.</param>
		/// <param name="context">The activation context.</param>
		/// <param name="interfaceId">The requested interface identifier.</param>
		/// <param name="instance">Receives the created object pointer.</param>
		/// <returns>The HRESULT returned by COM.</returns>
		[LibraryImport("ole32.dll", EntryPoint = "CoCreateInstance")]
		public static unsafe partial int CoCreateInstanceRaw(Guid* classId, nint outer, uint context, Guid* interfaceId, nint* instance);

		/// <summary>Gets a Windows Runtime activation factory using the raw ABI signature.</summary>
		/// <param name="activatableClassId">The runtime class name.</param>
		/// <param name="interfaceId">The requested factory interface identifier.</param>
		/// <param name="factory">Receives the activation factory pointer.</param>
		/// <returns>The HRESULT returned by Windows Runtime activation.</returns>
		[LibraryImport("combase.dll", EntryPoint = "RoGetActivationFactory")]
		public static unsafe partial int RoGetActivationFactoryRaw(nint activatableClassId, Guid* interfaceId, nint* factory);

		/// <summary>Creates a Shell item from a parsing name using the raw ABI signature.</summary>
		/// <param name="parsingName">The item parsing name.</param>
		/// <param name="bindContext">The optional bind context.</param>
		/// <param name="interfaceId">The requested interface identifier.</param>
		/// <param name="item">Receives the created item pointer.</param>
		/// <returns>The HRESULT returned by the Shell.</returns>
		[LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName")]
		public static unsafe partial int SHCreateItemFromParsingNameRaw(char* parsingName, nint bindContext, Guid* interfaceId, nint* item);

		/// <summary>Creates a stream for a file using the raw ABI signature.</summary>
		/// <param name="fileName">The file path.</param>
		/// <param name="mode">The desired access mode.</param>
		/// <param name="attributes">The file attributes.</param>
		/// <param name="create">Whether to create the file when it does not exist.</param>
		/// <param name="templateStream">The optional template stream.</param>
		/// <param name="stream">Receives the created stream pointer.</param>
		/// <returns>The HRESULT returned by the Shell.</returns>
		[LibraryImport("shlwapi.dll", EntryPoint = "SHCreateStreamOnFileEx", StringMarshalling = StringMarshalling.Utf16)]
		public static partial int SHCreateStreamOnFileExRaw(string fileName, uint mode, uint attributes, [MarshalAs(UnmanagedType.Bool)] bool create, nint templateStream, out nint stream);

		// NOTE:
		//  CsWin32 doesn't generate SetWindowLong on other than x86 and vice versa.
		//  For more info, visit https://github.com/microsoft/CsWin32/issues/882
		/// <summary>Sets a window long value using the pointer-sized Windows API.</summary>
		/// <param name="hWnd">The target window handle.</param>
		/// <param name="nIndex">The value index.</param>
		/// <param name="dwNewLong">The new value.</param>
		/// <returns>The previous value.</returns>
		public static unsafe nint SetWindowLongPtr(HWND hWnd, WINDOW_LONG_PTR_INDEX nIndex, nint dwNewLong)
		{
			return sizeof(nint) is 4
				? (nint)_SetWindowLong(hWnd, (int)nIndex, (int)dwNewLong)
				: _SetWindowLongPtr(hWnd, (int)nIndex, dwNewLong);
		}

		/// <summary>Refreshes the Recycle Bin icon.</summary>
		[LibraryImport("shell32.dll", EntryPoint = "SHUpdateRecycleBinIcon", SetLastError = true)]
		public static partial void SHUpdateRecycleBinIcon();

		[LibraryImport("User32", EntryPoint = "SetWindowLongW")]
		private static partial int _SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

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
