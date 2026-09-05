// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;

namespace Files.Core.Windows;

internal static class WindowsShellDataObjectFormat
{
	internal const string AsyncFlag = "AsyncFlag";
	internal const string PreferredDropEffect = "Preferred DropEffect";

	internal static void SetDword(IDataObject dataObject, string formatName, uint value)
	{
		_ = SetDwordCore(dataObject, formatName, value, bestEffort: false);
	}

	internal static bool TrySetDword(IDataObject dataObject, string formatName, uint value)
	{
		return SetDwordCore(dataObject, formatName, value, bestEffort: true);
	}

	internal static unsafe bool TryGetDword(IDataObject dataObject, string formatName, out uint value)
	{
		ArgumentNullException.ThrowIfNull(dataObject);

		ArgumentException.ThrowIfNullOrWhiteSpace(formatName);

		value = 0;
		var clipboardFormat = PInvoke.RegisterClipboardFormat(formatName);
		if (clipboardFormat is 0)
		{
			return false;
		}

		var format = default(FORMATETC);
		format.cfFormat = checked((ushort)clipboardFormat);
		format.dwAspect = (uint)DVASPECT.DVASPECT_CONTENT;
		format.lindex = -1;
		format.tymed = (uint)TYMED.TYMED_HGLOBAL;
		if (dataObject.GetData(in format, out var medium).Failed)
		{
			return false;
		}

		try
		{
			if (medium.tymed is not TYMED.TYMED_HGLOBAL || medium.u.hGlobal.IsNull || PInvoke.GlobalSize(medium.u.hGlobal) < sizeof(uint))
			{
				return false;
			}

			var buffer = PInvoke.GlobalLock(medium.u.hGlobal);
			if (buffer is null)
			{
				return false;
			}

			try
			{
				value = *(uint*)buffer;

				return true;
			}
			finally
			{
				_ = PInvoke.GlobalUnlock(medium.u.hGlobal);
			}
		}
		finally
		{
			PInvoke.ReleaseStgMedium(ref medium);
		}
	}

	private static unsafe bool SetDwordCore(IDataObject dataObject, string formatName, uint value, bool bestEffort)
	{
		ArgumentNullException.ThrowIfNull(dataObject);

		ArgumentException.ThrowIfNullOrWhiteSpace(formatName);

		var clipboardFormat = PInvoke.RegisterClipboardFormat(formatName);
		if (clipboardFormat is 0)
		{
			if (bestEffort)
			{
				return false;
			}

			throw new Win32Exception(Marshal.GetLastPInvokeError(), $"The {formatName} data-object format could not be registered.");
		}

		var memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE | GLOBAL_ALLOC_FLAGS.GMEM_ZEROINIT, sizeof(uint));
		if (memory.IsNull)
		{
			if (bestEffort)
			{
				return false;
			}

			throw new OutOfMemoryException($"Memory for the {formatName} data-object format could not be allocated.");
		}

		try
		{
			var buffer = PInvoke.GlobalLock(memory);
			if (buffer is null)
			{
				if (bestEffort)
				{
					return false;
				}

				throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Memory for the {formatName} data-object format could not be locked.");
			}

			try
			{
				*(uint*)buffer = value;
			}
			finally
			{
				_ = PInvoke.GlobalUnlock(memory);
			}

			var format = default(FORMATETC);
			format.cfFormat = checked((ushort)clipboardFormat);
			format.dwAspect = (uint)DVASPECT.DVASPECT_CONTENT;
			format.lindex = -1;
			format.tymed = (uint)TYMED.TYMED_HGLOBAL;

			var medium = default(STGMEDIUM);
			medium.tymed = TYMED.TYMED_HGLOBAL;
			medium.u.hGlobal = memory;

			var hr = dataObject.SetData(in format, in medium, true);
			if (hr.Failed)
			{
				if (!bestEffort)
				{
					hr.ThrowOnFailure();
				}

				return false;
			}

			memory = default;

			return true;
		}
		finally
		{
			if (!memory.IsNull)
			{
				PInvoke.GlobalFree(memory);
			}
		}
	}
}
