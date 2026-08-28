// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace Files.Core.Capabilities.Previews;

/// <summary>Creates controllers for Windows Shell preview handlers.</summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsShellPreviewHandlerControllerFactory : IWindowsPreviewHandlerControllerFactory
{
	private readonly IWindowsPreviewHandlerActivationPolicy _activationPolicy;

	/// <summary>Initializes a controller factory with the local-server policy.</summary>
	public WindowsShellPreviewHandlerControllerFactory()
		: this(new LocalServerWindowsPreviewHandlerActivationPolicy())
	{
	}

	/// <summary>Initializes a controller factory.</summary>
	/// <param name="activationPolicy">The activation policy.</param>
	public WindowsShellPreviewHandlerControllerFactory(IWindowsPreviewHandlerActivationPolicy activationPolicy)
	{
		ArgumentNullException.ThrowIfNull(activationPolicy);

		_activationPolicy = activationPolicy;
	}

	/// <summary>Creates a controller for a preview handler CLSID.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The created controller.</returns>
	public IWindowsPreviewHandlerController Create(Guid handlerClsid)
	{
		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		var activationContext = _activationPolicy.GetContext(handlerClsid);
		if (activationContext is 0)
		{
			throw new InvalidOperationException("The preview handler activation policy returned no activation context.");
		}

		return WindowsShellPreviewHandlerController.Create(handlerClsid, (uint)activationContext);
	}
}

[SupportedOSPlatform("windows6.0.6000")]
internal sealed unsafe class WindowsShellPreviewHandlerController
    : IWindowsPreviewHandlerController
{
	private const uint StorageModeRead = 0;
	private const int PreviewHandlerVisualsSetBackgroundColorSlot = 3;
	private const int PreviewHandlerVisualsSetTextColorSlot = 5;

	private static readonly Guid _iObjectWithSiteId =
		new("00000118-0000-0000-C000-000000000046");
	private static readonly Guid _iInitializeWithStreamId =
		new("B824B49D-22AC-4161-AC8A-9916E5FA3A8");
	private static readonly Guid _iInitializeWithItemId =
		new("7F73BE3F-FB79-493C-A6C7-7EE14E245841");
	private static readonly Guid _iInitializeWithFileId =
		new("B7D14566-0509-4CCE-A71F-0A554233BD9B");
	private static readonly Guid _iPreviewHandlerVisualsId =
		new("196BF9A5-B346-4EF0-AA1E-5DCDB76768B8");

	private void* _handler;
	private void* _initializedStream;
	private void* _initializedItem;
	private void* _previewHandlerFrame;
	private bool _didPreview;
	private bool _didUnload;
	private bool _isDisposed;

	private WindowsShellPreviewHandlerController(void* handler)
	{
		_handler = handler;
	}

	public static WindowsShellPreviewHandlerController Create(Guid handlerClsid, uint activationContext)
	{
		void* handler = null;
		var interfaceId = typeof(IPreviewHandler).GUID;
		nint handlerHandle = 0;
		HRESULT result;
		Guid* classIdPointer = &handlerClsid;
		Guid* interfaceIdPointer = &interfaceId;
		result = (HRESULT)PInvoke.CoCreateInstanceRaw(classIdPointer, nint.Zero, activationContext, interfaceIdPointer, &handlerHandle);
		handler = (void*)handlerHandle;

		if (result.Failed || handler is null)
		{
			Release(handler);
			throw new COMException("The Windows preview handler could not be activated.", result.Value);
		}

		return new WindowsShellPreviewHandlerController(handler);
	}

	public void SetSite()
	{
		EnsureActive();
		if (!TryQuery(_iObjectWithSiteId, out var siteInterface))
		{
			return;
		}

		var frame = WindowsPreviewHandlerFrame.Create();
		try
		{
			CallSetSite(siteInterface, (void*)frame).ThrowOnFailure();
			_previewHandlerFrame = (void*)frame;
		}
		catch
		{
			Release((void*)frame);
			throw;
		}
		finally
		{
			Release(siteInterface);
		}
	}

	public bool TryInitializeWithStream(string fileSystemPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileSystemPath);

		EnsureActive();

		if (!TryQuery(_iInitializeWithStreamId, out var initializer))
		{
			return false;
		}

		void* stream = null;
		try
		{
			var openResult = (HRESULT)PInvoke.SHCreateStreamOnFileExRaw(fileSystemPath, 0x00000040, 0, false, nint.Zero, out var streamHandle);
			stream = (void*)streamHandle;
			openResult.ThrowOnFailure();

			var initializeResult = CallInitializeWithStream(initializer, stream, 0);
			if (IsOptionalInitializationFailure(initializeResult))
			{
				return false;
			}

			initializeResult.ThrowOnFailure();
			_initializedStream = stream;
			stream = null;

			return true;
		}
		finally
		{
			Release(initializer);
			Release(stream);
		}
	}

	public bool TryInitializeWithItem(string parsingName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		EnsureActive();

		if (!TryQuery(_iInitializeWithItemId, out var initializer))
		{
			return false;
		}

		void* item = null;
		try
		{
			var interfaceId = typeof(IShellItem).GUID;
			nint itemHandle = 0;
			HRESULT createResult;
			fixed (char* parsingNamePointer = parsingName)
			{
				Guid* interfaceIdPointer = &interfaceId;
				createResult = (HRESULT)PInvoke.SHCreateItemFromParsingNameRaw(parsingNamePointer, nint.Zero, interfaceIdPointer, &itemHandle);
			}
			item = (void*)itemHandle;
			createResult.ThrowOnFailure();

			var initializeResult = CallInitializeWithItem(initializer, item, 0);
			if (IsOptionalInitializationFailure(initializeResult))
			{
				return false;
			}

			initializeResult.ThrowOnFailure();
			_initializedItem = item;
			item = null;

			return true;
		}
		finally
		{
			Release(initializer);
			Release(item);
		}
	}

	public bool TryInitializeWithFile(string fileSystemPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileSystemPath);

		EnsureActive();

		if (!TryQuery(_iInitializeWithFileId, out var initializer))
		{
			return false;
		}

		try
		{
			fixed (char* path = fileSystemPath)
			{
				var initializeResult = CallInitializeWithFile(initializer, path, StorageModeRead);
				if (IsOptionalInitializationFailure(initializeResult))
				{
					return false;
				}

				initializeResult.ThrowOnFailure();

				return true;
			}
		}
		finally
		{
			Release(initializer);
		}
	}

	public void SetWindow(nint windowHandle, WindowsPreviewBounds bounds)
	{
		EnsureActive();
		var rectangle = ToRect(bounds);
		CallSetWindow(_handler, (HWND)windowHandle, in rectangle).ThrowOnFailure();
	}

	public void SetBounds(WindowsPreviewBounds bounds)
	{
		EnsureActive();
		var rectangle = ToRect(bounds);
		CallSetRect(_handler, in rectangle).ThrowOnFailure();
	}

	public void SetTheme(WindowsPreviewColor background, WindowsPreviewColor foreground)
	{
		EnsureActive();
		if (!TryQuery(_iPreviewHandlerVisualsId, out var visuals))
		{
			return;
		}

		try
		{
			CallSetColor(visuals, PreviewHandlerVisualsSetBackgroundColorSlot, ToColorRef(background)).ThrowOnFailure();
			CallSetColor(visuals, PreviewHandlerVisualsSetTextColorSlot, ToColorRef(foreground)).ThrowOnFailure();
		}
		finally
		{
			Release(visuals);
		}
	}

	public void DoPreview()
	{
		EnsureActive();
		if (_didPreview)
		{
			throw new InvalidOperationException("DoPreview can only be called once for a session.");
		}

		CallDoPreview(_handler).ThrowOnFailure();
		_didPreview = true;
	}

	public void SetFocus()
	{
		EnsureActive();
		CallSetFocus(_handler).ThrowOnFailure();
	}

	public nint QueryFocus()
	{
		EnsureActive();

		return CallQueryFocus(_handler);
	}

	public bool TryTranslateAccelerator(nint messagePointer)
	{
		if (messagePointer == 0)
		{
			throw new ArgumentException("A native MSG pointer is required.", nameof(messagePointer));
		}

		EnsureActive();
		var result = CallTranslateAccelerator(_handler, (void*)messagePointer);
		if (result == HRESULT.S_FALSE)
		{
			return false;
		}

		result.ThrowOnFailure();

		return true;
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		var errors = new List<Exception>();

		if (_handler is not null)
		{
			if (!_didUnload)
			{
				TryCleanup(() => CallUnload(_handler).ThrowOnFailure(), errors);
				_didUnload = true;
			}

			TryCleanup(SetSiteForCleanup, errors);
			Release(_initializedStream);
			_initializedStream = null;
			Release(_initializedItem);
			_initializedItem = null;
			Release(_handler);
			_handler = null;
		}

		if (errors.Count is 1)
		{
			throw errors[0];
		}

		if (errors.Count > 1)
		{
			throw new AggregateException(errors);
		}
	}

	private void SetSiteForCleanup()
	{
		void* siteInterface = null;
		try
		{
			if (TryQuery(_iObjectWithSiteId, out siteInterface))
			{
				CallSetSite(siteInterface, null).ThrowOnFailure();
			}
		}
		finally
		{
			Release(siteInterface);
			Release(_previewHandlerFrame);
			_previewHandlerFrame = null;
		}
	}

	private bool TryQuery(Guid interfaceId, out void* interfacePointer)
	{
		var result = CallQueryInterface(_handler, interfaceId, out interfacePointer);
		if (result == HRESULT.E_NOINTERFACE)
		{
			interfacePointer = null;

			return false;
		}

		result.ThrowOnFailure();

		return true;
	}

	private void EnsureActive()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (_handler is null)
		{
			throw new ObjectDisposedException(nameof(WindowsShellPreviewHandlerController));
		}
	}

	private static bool IsOptionalInitializationFailure(HRESULT result)
	{
		return result == HRESULT.E_NOINTERFACE || result == HRESULT.E_NOTIMPL;
	}

	private static RECT ToRect(WindowsPreviewBounds bounds)
	{
		return new RECT
		{
			left = bounds.X,
			top = bounds.Y,
			right = checked(bounds.X + bounds.Width),
			bottom = checked(bounds.Y + bounds.Height),
		};
	}

	private static uint ToColorRef(WindowsPreviewColor color)
	{
		return (uint)(color.Red | (color.Green << 8) | (color.Blue << 16));
	}

	private static void TryCleanup(Action action, ICollection<Exception> errors)
	{
		try
		{
			action();
		}
		catch (Exception exception)
		{
			errors.Add(exception);
		}
	}

	private static void Release(void* pointer)
	{
		if (pointer is null)
		{
			return;
		}

		var vtable = *(void***)pointer;
		var release = (delegate* unmanaged[Stdcall]<void*, uint>)vtable[2];
		_ = release(pointer);
	}

	private static HRESULT CallSetSite(void* pointer, void* site)
	{
		var vtable = *(void***)pointer;
		var setSite = (delegate* unmanaged[Stdcall]<void*, void*, HRESULT>)vtable[3];

		return setSite(pointer, site);
	}

	private static HRESULT CallQueryInterface(void* pointer, Guid interfaceId, out void* resultPointer)
	{
		var vtable = *(void***)pointer;
		var queryInterface = (delegate* unmanaged[Stdcall]<void*, Guid*, void**, HRESULT>)vtable[0];
		void* queriedPointer = null;
		var result = queryInterface(pointer, &interfaceId, &queriedPointer);
		resultPointer = queriedPointer;

		return result;
	}

	private static HRESULT CallSetWindow(void* pointer, HWND windowHandle, in RECT rectangle)
	{
		var vtable = *(void***)pointer;
		var setWindow = (delegate* unmanaged[Stdcall]<void*, HWND, RECT*, HRESULT>)vtable[3];
		var rectangleCopy = rectangle;

		return setWindow(pointer, windowHandle, &rectangleCopy);
	}

	private static HRESULT CallSetRect(void* pointer, in RECT rectangle)
	{
		var vtable = *(void***)pointer;
		var setRect = (delegate* unmanaged[Stdcall]<void*, RECT*, HRESULT>)vtable[4];
		var rectangleCopy = rectangle;

		return setRect(pointer, &rectangleCopy);
	}

	private static HRESULT CallDoPreview(void* pointer)
	{
		var vtable = *(void***)pointer;
		var doPreview = (delegate* unmanaged[Stdcall]<void*, HRESULT>)vtable[5];

		return doPreview(pointer);
	}

	private static HRESULT CallUnload(void* pointer)
	{
		var vtable = *(void***)pointer;
		var unload = (delegate* unmanaged[Stdcall]<void*, HRESULT>)vtable[6];

		return unload(pointer);
	}

	private static HRESULT CallSetFocus(void* pointer)
	{
		var vtable = *(void***)pointer;
		var setFocus = (delegate* unmanaged[Stdcall]<void*, HRESULT>)vtable[7];

		return setFocus(pointer);
	}

	private static nint CallQueryFocus(void* pointer)
	{
		var vtable = *(void***)pointer;
		var queryFocus = (delegate* unmanaged[Stdcall]<void*, HWND*, HRESULT>)vtable[8];
		HWND focus;
		var result = queryFocus(pointer, &focus);
		result.ThrowOnFailure();

		return focus;
	}

	private static HRESULT CallTranslateAccelerator(void* pointer, void* message)
	{
		var vtable = *(void***)pointer;
		var translate = (delegate* unmanaged[Stdcall]<void*, void*, HRESULT>)vtable[9];

		return translate(pointer, message);
	}

	private static HRESULT CallInitializeWithStream(void* pointer, void* stream, uint mode)
	{
		var vtable = *(void***)pointer;
		var initialize = (delegate* unmanaged[Stdcall]<void*, void*, uint, HRESULT>)vtable[3];

		return initialize(pointer, stream, mode);
	}

	private static HRESULT CallInitializeWithItem(void* pointer, void* item, uint mode)
	{
		var vtable = *(void***)pointer;
		var initialize = (delegate* unmanaged[Stdcall]<void*, void*, uint, HRESULT>)vtable[3];

		return initialize(pointer, item, mode);
	}

	private static HRESULT CallInitializeWithFile(void* pointer, char* path, uint mode)
	{
		var vtable = *(void***)pointer;
		var initialize = (delegate* unmanaged[Stdcall]<void*, char*, uint, HRESULT>)vtable[3];

		return initialize(pointer, path, mode);
	}

	private static HRESULT CallSetColor(void* pointer, int slot, uint color)
	{
		var vtable = *(void***)pointer;
		var setColor = (delegate* unmanaged[Stdcall]<void*, uint, HRESULT>)vtable[slot];

		return setColor(pointer, color);
	}
}
