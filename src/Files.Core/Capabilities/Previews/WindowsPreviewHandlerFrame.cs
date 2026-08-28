// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace Files.Core.Capabilities.Previews;

/// <summary>
/// Minimal in-process COM site exposed to a preview handler.
/// </summary>
internal sealed unsafe class WindowsPreviewHandlerFrame
{
	private static readonly Guid _iUnknownId = new("00000000-0000-0000-C000-000000000046");

	private static readonly Guid _iPreviewHandlerFrameId = new("FEC87AAF-35F9-447A-ADB7-20234491401A");

	private readonly GCHandle _handle;

	private nint _instance;

	private nint _vtable;

	private int _referenceCount = 1;

	private int _isDisposed;

	private WindowsPreviewHandlerFrame()
	{
		_handle = GCHandle.Alloc(this);
	}

	public static nint Create()
	{
		var frame = new WindowsPreviewHandlerFrame();
		try
		{
			frame._vtable = Marshal.AllocHGlobal(IntPtr.Size * 5);
			frame._instance = Marshal.AllocHGlobal(IntPtr.Size * 2);

			var vtable = (nint*)frame._vtable;
			vtable[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface;
			vtable[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
			vtable[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release;
			vtable[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetWindowContext;
			vtable[4] = (nint)(delegate* unmanaged[Stdcall]<nint, void*, int>)&TranslateAccelerator;

			var instance = (nint*)frame._instance;
			instance[0] = frame._vtable;
			instance[1] = GCHandle.ToIntPtr(frame._handle);

			return frame._instance;
		}
		catch
		{
			frame.DisposeUnmanaged();
			throw;
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int QueryInterface(nint instance, Guid* interfaceId, nint* result)
	{
		if (interfaceId is null || result is null)
		{
			return HRESULT.E_POINTER;
		}

		*result = 0;
		if (*interfaceId != _iUnknownId && *interfaceId != _iPreviewHandlerFrameId)
		{
			return HRESULT.E_NOINTERFACE;
		}

		var frame = GetFrame(instance);
		if (frame is null)
		{
			return HRESULT.E_FAIL;
		}

		_ = Interlocked.Increment(ref frame._referenceCount);
		*result = instance;

		return HRESULT.S_OK;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static uint AddRef(nint instance)
	{
		var frame = GetFrame(instance);

		return frame is null
			? 0
			: (uint)Interlocked.Increment(ref frame._referenceCount);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static uint Release(nint instance)
	{
		var frame = GetFrame(instance);
		if (frame is null)
		{
			return 0;
		}

		var count = Interlocked.Decrement(ref frame._referenceCount);
		if (count <= 0)
		{
			frame.DisposeUnmanaged();

			return 0;
		}

		return (uint)count;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int GetWindowContext(nint instance, nint* frameInfo)
	{
		if (frameInfo is null)
		{
			return HRESULT.E_POINTER;
		}

		// PREVIEWHANDLERFRAMEINFO contains an accelerator handle and a count.
		NativeMemory.Clear(frameInfo, (nuint)(IntPtr.Size * 2));

		return GetFrame(instance) is null ? HRESULT.E_FAIL : HRESULT.S_OK;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int TranslateAccelerator(nint instance, void* message)
	{
		// The future UI adapter owns keyboard routing. S_FALSE tells the handler
		// that this frame did not consume the message.
		return GetFrame(instance) is null
			? HRESULT.E_FAIL
			: HRESULT.S_FALSE;
	}

	private static WindowsPreviewHandlerFrame? GetFrame(nint instance)
	{
		if (instance == 0)
		{
			return null;
		}

		try
		{
			var objectData = (nint*)instance;
			var handle = GCHandle.FromIntPtr(objectData[1]);

			return handle.Target as WindowsPreviewHandlerFrame;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	private void DisposeUnmanaged()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		var instance = _instance;
		_instance = 0;
		if (instance != 0)
		{
			Marshal.FreeHGlobal(instance);
		}

		var vtable = _vtable;
		_vtable = 0;
		if (vtable != 0)
		{
			Marshal.FreeHGlobal(vtable);
		}

		if (_handle.IsAllocated)
		{
			_handle.Free();
		}
	}
}
