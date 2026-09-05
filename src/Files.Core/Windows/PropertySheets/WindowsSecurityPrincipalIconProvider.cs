// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace Files.Core.Windows;

internal static unsafe class WindowsSecurityPrincipalIconProvider
{
	private const int SidImageResourceId = 101;
	private const string SidImageResourceType = "Image";
	private static readonly Lazy<ReadOnlyMemory<byte>> _imageStrip = new(LoadImageStrip);

	internal static (ReadOnlyMemory<byte> Data, int Index) GetIcon(string sid, SID_NAME_USE type)
	{
		var index = GetIconIndex(sid, type);

		return WindowsThumbnailRenderer.TryCropEncodedImage(_imageStrip.Value, index * 16, 0, 16, 16, out var icon, CancellationToken.None)
			? (icon, 0)
			: (ReadOnlyMemory<byte>.Empty, 0);
	}

	private static int GetIconIndex(string sid, SID_NAME_USE type)
	{
		if (sid.StartsWith("S-1-15-2-", StringComparison.Ordinal))
		{
			return 7;
		}

		if (sid.StartsWith("S-1-15-3-", StringComparison.Ordinal))
		{
			return 6;
		}

		return type switch
		{
			SID_NAME_USE.SidTypeUser => 4,
			SID_NAME_USE.SidTypeGroup or SID_NAME_USE.SidTypeAlias or SID_NAME_USE.SidTypeWellKnownGroup => 2,
			SID_NAME_USE.SidTypeComputer => 1,
			_ => 0,
		};
	}

	private static ReadOnlyMemory<byte> LoadImageStrip()
	{
		var modulePath = Path.Combine(Environment.SystemDirectory, "aclui.dll");
		HMODULE module;
		fixed (char* modulePathPointer = modulePath)
		{
			module = PInvoke.LoadLibrary(new PCWSTR(modulePathPointer));
		}

		if (module.IsNull)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		try
		{
			fixed (char* typePointer = SidImageResourceType)
			{
				var resource = PInvoke.FindResource(module, new PCWSTR((char*)SidImageResourceId), new PCWSTR(typePointer));
				if (resource.IsNull)
				{
					return ReadOnlyMemory<byte>.Empty;
				}

				var size = PInvoke.SizeofResource(module, resource);
				var resourceData = PInvoke.LoadResource(module, resource);
				var data = resourceData.IsNull ? null : PInvoke.LockResource(resourceData);
				if (size is 0 || size > int.MaxValue || data is null)
				{
					return ReadOnlyMemory<byte>.Empty;
				}

				return new ReadOnlySpan<byte>(data, checked((int)size)).ToArray();
			}
		}
		finally
		{
			PInvoke.FreeLibrary(module);
		}
	}
}
