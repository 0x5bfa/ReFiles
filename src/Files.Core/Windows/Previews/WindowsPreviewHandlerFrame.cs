// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Windows;

/// <summary>
/// Minimal in-process COM site exposed to a preview handler.
/// </summary>
[GeneratedComClass]
internal sealed unsafe partial class WindowsPreviewHandlerFrame : IPreviewHandlerFrame
{
	private const ushort FirstLetterKey = 0x41;
	private const ushort LastLetterKey = 0x5A;
	private const ushort FirstFunctionKey = 0x70;
	private const ushort LastFunctionKey = 0x7B;
	private const ushort TabKey = 0x09;
	private const int AcceleratorCount = 66;

	private readonly HWND _hostWindow;
	private readonly WindowsPreviewAcceleratorForwarder? _acceleratorForwarder;

	internal WindowsPreviewHandlerFrame(HWND hostWindow, WindowsPreviewAcceleratorForwarder? acceleratorForwarder)
	{
		_hostWindow = hostWindow;
		_acceleratorForwarder = acceleratorForwarder;
	}

	/// <inheritdoc />
	public HRESULT GetWindowContext(PREVIEWHANDLERFRAMEINFO* frameInfo)
	{
		if (frameInfo is null)
		{
			return HRESULT.E_POINTER;
		}

		*frameInfo = default;
		var accelerators = CreateAccelerators();
		fixed (ACCEL* acceleratorPointer = accelerators)
		{
			var acceleratorTable = PInvoke.CreateAcceleratorTable(acceleratorPointer, accelerators.Length);
			if (acceleratorTable.IsNull)
			{
				return HRESULT.E_FAIL;
			}

			var copiedCount = PInvoke.CopyAcceleratorTable(acceleratorTable, null, 0);
			if (copiedCount != accelerators.Length)
			{
				PInvoke.DestroyAcceleratorTable(acceleratorTable);

				return HRESULT.E_FAIL;
			}

			frameInfo->haccel = acceleratorTable;
			frameInfo->cAccelEntries = (uint)copiedCount;
		}

		return HRESULT.S_OK;
	}

	/// <inheritdoc />
	public HRESULT TranslateAccelerator(MSG* message)
	{
		if (message is null)
		{
			return HRESULT.E_POINTER;
		}

		if (_acceleratorForwarder is null)
		{
			return HRESULT.S_FALSE;
		}

		var messageCopy = *message;
		messageCopy.hwnd = _hostWindow;
		try
		{
			return _acceleratorForwarder(in messageCopy) ? HRESULT.S_OK : HRESULT.S_FALSE;
		}
		catch
		{
			return HRESULT.E_FAIL;
		}
	}

	private static ACCEL[] CreateAccelerators()
	{
		var accelerators = new ACCEL[AcceleratorCount];
		var index = 0;
		for (var key = FirstLetterKey; key <= LastLetterKey; key++)
		{
			accelerators[index++] = CreateAccelerator(ACCEL_VIRT_FLAGS.FVIRTKEY | ACCEL_VIRT_FLAGS.FALT, key);
		}

		for (var key = FirstLetterKey; key <= LastLetterKey; key++)
		{
			accelerators[index++] = CreateAccelerator(ACCEL_VIRT_FLAGS.FVIRTKEY | ACCEL_VIRT_FLAGS.FCONTROL, key);
		}

		for (var key = FirstFunctionKey; key <= LastFunctionKey; key++)
		{
			accelerators[index++] = CreateAccelerator(ACCEL_VIRT_FLAGS.FVIRTKEY, key);
		}

		accelerators[index++] = CreateAccelerator(ACCEL_VIRT_FLAGS.FVIRTKEY, TabKey);
		accelerators[index] = CreateAccelerator(ACCEL_VIRT_FLAGS.FVIRTKEY | ACCEL_VIRT_FLAGS.FSHIFT, TabKey);

		return accelerators;
	}

	private static ACCEL CreateAccelerator(ACCEL_VIRT_FLAGS flags, ushort key)
	{
		ACCEL accelerator = default;
		accelerator.fVirt = flags;
		accelerator.key = key;
		accelerator.cmd = 0;

		return accelerator;
	}
}
