// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace Windows.Win32.Extras;

/// <summary>Delegate used as a managed window procedure callback.</summary>
/// <param name="hWnd">The window handle.</param>
/// <param name="msg">The window message.</param>
/// <param name="wParam">The message parameter.</param>
/// <param name="lParam">The message parameter.</param>
/// <returns>The result of processing the message.</returns>
[UnmanagedFunctionPointer(CallingConvention.Winapi)]
public delegate LRESULT ManagedWNDPROC(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam);
