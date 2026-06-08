// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Files.Core.ItemFeatures.Previews;

/// <summary>
/// Minimal in-process COM site exposed to a preview handler.
/// </summary>
internal sealed unsafe class WindowsPreviewHandlerFrame
{
	private static readonly Guid IUnknownId =
		new("00000000-0000-0000-C000-000000000046");
	private static readonly Guid IPreviewHandlerFrameId =
		new("FEC87AAF-35F9-447A-ADB7-20234491401A");

	private const int S_OK = 0;
	private const int S_FALSE = 1;
	private const int E_NOINTERFACE = unchecked((int)0x80004002);
	private const int E_POINTER = unchecked((int)0x80004003);
	private const int E_FAIL = unchecked((int)0x80004005);

	private readonly GCHandle handle;
	private nint instance;
	private nint vtable;
	private int referenceCount = 1;
	private int isDisposed;

	private WindowsPreviewHandlerFrame()
	{
		handle = GCHandle.Alloc(this);
	}

	public static nint Create()
	{
		var frame = new WindowsPreviewHandlerFrame();
		try
		{
			frame.vtable = Marshal.AllocHGlobal(IntPtr.Size * 5);
			frame.instance = Marshal.AllocHGlobal(IntPtr.Size * 2);

			var vtable = (nint*)frame.vtable;
			vtable[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface;
			vtable[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
			vtable[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release;
			vtable[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetWindowContext;
			vtable[4] = (nint)(delegate* unmanaged[Stdcall]<nint, void*, int>)&TranslateAccelerator;

			var instance = (nint*)frame.instance;
			instance[0] = frame.vtable;
			instance[1] = GCHandle.ToIntPtr(frame.handle);
			return frame.instance;
		}
		catch
		{
			frame.DisposeUnmanaged();
			throw;
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int QueryInterface(
		nint instance,
		Guid* interfaceId,
		nint* result)
	{
		if (interfaceId is null || result is null)
		{
			return E_POINTER;
		}

		*result = 0;
		if (*interfaceId != IUnknownId && *interfaceId != IPreviewHandlerFrameId)
		{
			return E_NOINTERFACE;
		}

		var frame = GetFrame(instance);
		if (frame is null)
		{
			return E_FAIL;
		}

		_ = Interlocked.Increment(ref frame.referenceCount);
		*result = instance;
		return S_OK;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static uint AddRef(nint instance)
	{
		var frame = GetFrame(instance);
		return frame is null
			? 0
			: (uint)Interlocked.Increment(ref frame.referenceCount);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static uint Release(nint instance)
	{
		var frame = GetFrame(instance);
		if (frame is null)
		{
			return 0;
		}

		var count = Interlocked.Decrement(ref frame.referenceCount);
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
			return E_POINTER;
		}

		// PREVIEWHANDLERFRAMEINFO contains an accelerator handle and a count.
		NativeMemory.Clear(frameInfo, (nuint)(IntPtr.Size * 2));
		return GetFrame(instance) is null ? E_FAIL : S_OK;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int TranslateAccelerator(nint instance, void* message)
	{
		// The future UI adapter owns keyboard routing. S_FALSE tells the handler
		// that this frame did not consume the message.
		return GetFrame(instance) is null
			? E_FAIL
			: S_FALSE;
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
		if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
		{
			return;
		}

		var instance = this.instance;
		this.instance = 0;
		if (instance != 0)
		{
			Marshal.FreeHGlobal(instance);
		}

		var vtable = this.vtable;
		this.vtable = 0;
		if (vtable != 0)
		{
			Marshal.FreeHGlobal(vtable);
		}

		if (handle.IsAllocated)
		{
			handle.Free();
		}
	}
}
