// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Files.Core.Capabilities;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

internal sealed class WindowsPreviewEnterpriseIdResolver : IWindowsPreviewEnterpriseIdResolver
{
	public bool HasEnterpriseId(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.CoreModel is not IWindowsStorable item || context.CoreModel is not IFile || string.IsNullOrWhiteSpace(item.ParsingName))
		{
			return false;
		}

		try
		{
			var hr = PInvoke.SHCreateItemFromParsingName(item.ParsingName, null, out IShellItem shellItem);
			if (hr != HRESULT.S_OK || shellItem is not IShellItem2 shellItem2)
			{
				return false;
			}

			return HasEnterpriseId(shellItem2);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException or InvalidOperationException or NotSupportedException or SecurityException)
		{
			return false;
		}
	}

	private static unsafe bool HasEnterpriseId(IShellItem2 shellItem)
	{
		var hr = shellItem.GetString(in PInvoke.PKEY_Security_EncryptionOwners, out var enterpriseId);
		if (hr.Failed)
		{
			return false;
		}

		try
		{
			return !string.IsNullOrEmpty(enterpriseId.ToString());
		}
		finally
		{
			PInvoke.CoTaskMemFree(enterpriseId.Value);
		}
	}
}
