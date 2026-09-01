// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Interop.Windows;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace Files.Core.Storage.Windows;

internal static class WindowsElevationMoniker
{
	internal static HRESULT Create<T>(HWND owner, Guid classId, out T? instance) where T : class
	{
		instance = null;
		var displayName = $"Elevation:Administrator!new:{classId:B}";
		var bindOptions = new BIND_OPTS3
		{
			Base = new BIND_OPTS2
			{
				Base = new BIND_OPTS { cbStruct = checked((uint)System.Runtime.InteropServices.Marshal.SizeOf<BIND_OPTS3>()) },
				dwClassContext = (uint)CLSCTX.CLSCTX_LOCAL_SERVER,
			},
			hwnd = owner,
		};

		return ComActivationNativeMethods.CoGetObject(displayName, ref bindOptions, out instance);
	}
}
