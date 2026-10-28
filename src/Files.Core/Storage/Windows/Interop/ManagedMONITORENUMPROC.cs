// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Windows.Win32.Extras;

/// <summary>Delegate used to enumerate monitors through the Win32 API.</summary>
/// <param name="param0">The monitor handle.</param>
/// <param name="param1">The device context handle.</param>
/// <param name="param2">The monitor rectangle.</param>
/// <param name="param3">The application-defined value.</param>
/// <returns>A nonzero value to continue enumeration.</returns>
[UnmanagedFunctionPointer(CallingConvention.Winapi)]
public delegate BOOL ManagedMONITORENUMPROC([In] HMONITOR param0, [In] HDC param1, [In, Out] ref RECT param2, [In] LPARAM param3);
