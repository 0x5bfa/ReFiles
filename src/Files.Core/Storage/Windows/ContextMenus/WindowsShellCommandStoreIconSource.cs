// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

internal static class WindowsShellCommandStoreIconSource
{
	internal static unsafe ReadOnlyMemory<byte> Read(string canonicalName, IShellItemArray selection, object? site, int pixelSize)
	{
		var hr = PInvoke.CoCreateInstance(typeof(CommandStoreCoclass).GUID, null, CLSCTX.CLSCTX_INPROC_SERVER, out ICommandStore? store);
		if (hr.Failed || store is null)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		hr = store.GetCanonicalCommand(canonicalName, typeof(IExplorerCommand).GUID, out var command);
		if (hr.Failed || command is null)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		var objectWithSite = command as IObjectWithSite;
		PWSTR value = default;
		try
		{
			if (site is not null)
			{
				objectWithSite?.SetSite(site);
			}

			hr = command.GetIcon(selection, &value);
			if (hr.Failed || value.Value is null)
			{
				return ReadOnlyMemory<byte>.Empty;
			}

			var (path, index) = WindowsShellExplorerCommandSource.ParseCommandIcon(value.ToString());

			return path is null ? ReadOnlyMemory<byte>.Empty : WindowsShellIconProvider.GetAppExtensionIcon(path, index, pixelSize, CancellationToken.None);
		}
		catch (COMException)
		{
			return ReadOnlyMemory<byte>.Empty;
		}
		finally
		{
			PInvoke.CoTaskMemFree(value.Value);
			objectWithSite?.SetSite(null!);
		}
	}
}
