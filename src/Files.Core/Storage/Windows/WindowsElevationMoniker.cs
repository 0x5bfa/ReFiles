// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace Files.Core.Storage.Windows;

internal static unsafe class WindowsElevationMoniker
{
	internal static HRESULT Create(HWND owner, Guid classId, Guid interfaceId, void** instance)
	{
		ArgumentNullException.ThrowIfNull(instance);

		*instance = null;
		var displayName = $"Elevation:Administrator!new:{classId:B}";
		var bindOptions = new BIND_OPTS3
		{
			Base = new BIND_OPTS2
			{
				Base = new BIND_OPTS { cbStruct = checked((uint)sizeof(BIND_OPTS3)) },
				dwClassContext = (uint)CLSCTX.CLSCTX_LOCAL_SERVER,
			},
			hwnd = owner,
		};
		fixed (char* displayNamePointer = displayName)
		{
			return (HRESULT)PInvoke.CoGetObjectRaw(displayNamePointer, &bindOptions, &interfaceId, (nint*)instance);
		}
	}
}
