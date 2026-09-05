// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using System.Security;
using Microsoft.Win32;

namespace Files.Core.Windows;

internal sealed class WindowsPreviewHandlerTrustResolver : IWindowsPreviewHandlerTrustResolver
{
	private const string AutomaticallyPreviewUntrustedFilesValue = "AutomaticallyPreviewUntrustedFiles";

	public bool AllowsUntrustedPreviews(Guid handlerClsid)
	{
		try
		{
			using var key = Registry.ClassesRoot.OpenSubKey($"CLSID\\{handlerClsid:B}", writable: false);

			return key?.GetValue(AutomaticallyPreviewUntrustedFilesValue) is int value && value == 1;
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
		{
			return false;
		}
	}
}
