// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using Files.Core.Interop.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.PropertiesSystem;
using Windows.Win32.UI.WindowsAndMessaging;

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
internal sealed unsafe class WindowsShellPreviewHandlerController : IWindowsPreviewHandlerController
{
	private IPreviewHandler? _handler;
	private IStream? _initializedStream;
	private IShellItem? _initializedItem;
	private WindowsPreviewHandlerFrame? _previewHandlerFrame;
	private bool _didPreview;
	private bool _didUnload;
	private bool _isDisposed;

	private WindowsShellPreviewHandlerController(IPreviewHandler handler)
	{
		_handler = handler;
	}

	/// <summary>Creates a controller for an activated preview handler.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <param name="activationContext">The COM activation context.</param>
	/// <returns>The created controller.</returns>
	public static WindowsShellPreviewHandlerController Create(Guid handlerClsid, uint activationContext)
	{
		var result = ComActivationNativeMethods.CoCreateInstance<IPreviewHandler>(handlerClsid, (CLSCTX)activationContext, out var handler);
		if (result.Failed || handler is null)
		{
			ReleaseComObject(handler);
			throw new COMException("The Windows preview handler could not be activated.", result.Value);
		}

		return new WindowsShellPreviewHandlerController(handler);
	}

	/// <inheritdoc />
	public void SetSite()
	{
		EnsureActive();
		if (!TryQueryInterface(_handler, out IObjectWithSite? siteInterface))
		{
			return;
		}

		var frame = new WindowsPreviewHandlerFrame();
		siteInterface.SetSite(frame).ThrowOnFailure();
		_previewHandlerFrame = frame;
	}

	/// <inheritdoc />
	public bool TryInitializeWithStream(string fileSystemPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileSystemPath);

		EnsureActive();

		if (!TryQueryInterface(_handler, out IInitializeWithStream? initializer))
		{
			return false;
		}

		IStream? stream = null;
		try
		{
			var openResult = PreviewHandlerNativeMethods.SHCreatePreviewStream(fileSystemPath, (uint)(STGM.STGM_READ | STGM.STGM_SHARE_DENY_NONE), out var createdStream);
			stream = createdStream;
			openResult.ThrowOnFailure();

			var initializeResult = initializer.Initialize(stream, (uint)STGM.STGM_READ);
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
			ReleaseComObject(stream);
		}
	}

	/// <inheritdoc />
	public bool TryInitializeWithItem(string parsingName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		EnsureActive();

		if (!TryQueryInterface(_handler, out IInitializeWithItem? initializer))
		{
			return false;
		}

		IShellItem? item = null;
		try
		{
			var createResult = PreviewHandlerNativeMethods.SHCreatePreviewItem(parsingName, out var createdItem);
			item = createdItem;
			createResult.ThrowOnFailure();

			var initializeResult = initializer.Initialize(item, (uint)STGM.STGM_READ);
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
			ReleaseComObject(item);
		}
	}

	/// <inheritdoc />
	public bool TryInitializeWithFile(string fileSystemPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileSystemPath);

		EnsureActive();

		if (!TryQueryInterface(_handler, out IInitializeWithFile? initializer))
		{
			return false;
		}

		fixed (char* path = fileSystemPath)
		{
			var initializeResult = initializer.Initialize(path, (uint)STGM.STGM_READ);
			if (IsOptionalInitializationFailure(initializeResult))
			{
				return false;
			}

			initializeResult.ThrowOnFailure();

			return true;
		}
	}

	/// <inheritdoc />
	public void SetWindow(nint windowHandle, WindowsPreviewBounds bounds)
	{
		EnsureActive();
		var rectangle = ToRect(bounds);
		_handler.SetWindow((HWND)windowHandle, &rectangle).ThrowOnFailure();
	}

	/// <inheritdoc />
	public void SetBounds(WindowsPreviewBounds bounds)
	{
		EnsureActive();
		var rectangle = ToRect(bounds);
		_handler.SetRect(&rectangle).ThrowOnFailure();
	}

	/// <inheritdoc />
	public void SetTheme(WindowsPreviewColor background, WindowsPreviewColor foreground)
	{
		EnsureActive();
		if (!TryQueryInterface(_handler, out IPreviewHandlerVisuals? visuals))
		{
			return;
		}

		visuals.SetBackgroundColor((COLORREF)ToColorRef(background)).ThrowOnFailure();
		visuals.SetTextColor((COLORREF)ToColorRef(foreground)).ThrowOnFailure();
	}

	/// <inheritdoc />
	public void DoPreview()
	{
		EnsureActive();
		if (_didPreview)
		{
			throw new InvalidOperationException("DoPreview can only be called once for a session.");
		}

		_handler.DoPreview().ThrowOnFailure();
		_didPreview = true;
	}

	/// <inheritdoc />
	public void SetFocus()
	{
		EnsureActive();
		_handler.SetFocus().ThrowOnFailure();
	}

	/// <inheritdoc />
	public nint QueryFocus()
	{
		EnsureActive();
		HWND focus;
		_handler.QueryFocus(&focus).ThrowOnFailure();

		return focus;
	}

	/// <inheritdoc />
	public bool TryTranslateAccelerator(nint messagePointer)
	{
		if (messagePointer == 0)
		{
			throw new ArgumentException("A native MSG pointer is required.", nameof(messagePointer));
		}

		EnsureActive();
		var result = _handler.TranslateAccelerator((MSG*)messagePointer);
		if (result == HRESULT.S_FALSE)
		{
			return false;
		}

		result.ThrowOnFailure();

		return true;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		var errors = new List<Exception>();
		var handler = _handler;

		if (handler is not null)
		{
			if (!_didUnload)
			{
				TryCleanup(() => handler.Unload().ThrowOnFailure(), errors);
				_didUnload = true;
			}

			TryCleanup(SetSiteForCleanup, errors);
			ReleaseComObject(_initializedStream);
			_initializedStream = null;
			ReleaseComObject(_initializedItem);
			_initializedItem = null;
			ReleaseComObject(handler);
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
		try
		{
			if (TryQueryInterface(_handler!, out IObjectWithSite? siteInterface))
			{
				siteInterface.SetSite(null!).ThrowOnFailure();
			}
		}
		finally
		{
			_previewHandlerFrame = null;
		}
	}

	[MemberNotNull(nameof(_handler))]
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

	private static bool TryQueryInterface<T>(object instance, [NotNullWhen(true)] out T? result) where T : class
	{
		try
		{
			result = (T)instance;

			return true;
		}
		catch (InvalidCastException exception) when (exception.HResult == HRESULT.E_NOINTERFACE.Value)
		{
			result = null;

			return false;
		}
	}

	private static void ReleaseComObject(object? value)
	{
		if (value is ComObject comObject)
		{
			comObject.FinalRelease();
		}
	}
}
