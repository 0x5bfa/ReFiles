// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using Windows.Win32.System.WinRT;

namespace Windows.Win32;

/// <summary>Extends the generated HSTRING safe handle with ownership transfer for custom marshalling.</summary>
public partial class WindowsDeleteStringSafeHandle
{
	internal HSTRING Detach()
	{
		var value = (HSTRING)DangerousGetHandle();
		SetHandleAsInvalid();

		return value;
	}
}
