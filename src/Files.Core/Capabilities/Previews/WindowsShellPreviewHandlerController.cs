// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
internal sealed class WindowsShellPreviewHandlerController : IWindowsPreviewHandlerController
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
		var hr = PInvoke.CoCreateInstance(in handlerClsid, null, (CLSCTX)activationContext, out IPreviewHandler handler);
		if (hr.Failed || handler is null)
		{
			throw new COMException("The Windows preview handler could not be activated.", hr.Value);
		}

		return new WindowsShellPreviewHandlerController(handler);
	}

	/// <inheritdoc />
	public void SetSite()
	{
		EnsureActive();
		var siteInterface = _handler as IObjectWithSite;
		if (siteInterface is null)
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

		var initializer = _handler as IInitializeWithStream;
		if (initializer is null)
		{
			return false;
		}

		var hr = PInvoke.SHCreateStreamOnFileEx(fileSystemPath, (uint)(STGM.STGM_READ | STGM.STGM_SHARE_DENY_NONE), 0, false, null!, out IStream stream);
		hr.ThrowOnFailure();

		hr = initializer.Initialize(stream, (uint)STGM.STGM_READ);
		if (IsOptionalInitializationFailure(hr))
		{
			return false;
		}

		hr.ThrowOnFailure();
		_initializedStream = stream;

		return true;
	}

	/// <inheritdoc />
	public bool TryInitializeWithItem(string parsingName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		EnsureActive();

		var initializer = _handler as IInitializeWithItem;
		if (initializer is null)
		{
			return false;
		}

		var hr = PInvoke.SHCreateItemFromParsingName(parsingName, null, out IShellItem item);
		hr.ThrowOnFailure();

		hr = initializer.Initialize(item, (uint)STGM.STGM_READ);
		if (IsOptionalInitializationFailure(hr))
		{
			return false;
		}

		hr.ThrowOnFailure();
		_initializedItem = item;

		return true;
	}

	/// <inheritdoc />
	public bool TryInitializeWithFile(string fileSystemPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileSystemPath);

		EnsureActive();

		var initializer = _handler as IInitializeWithFile;
		if (initializer is null)
		{
			return false;
		}

		var hr = initializer.Initialize(fileSystemPath, (uint)STGM.STGM_READ);
		if (IsOptionalInitializationFailure(hr))
		{
			return false;
		}

		hr.ThrowOnFailure();

		return true;
	}

	/// <inheritdoc />
	public void SetWindow(HWND windowHandle, WindowsPreviewBounds bounds)
	{
		EnsureActive();
		var rectangle = ToRect(bounds);
		_handler.SetWindow(windowHandle, in rectangle).ThrowOnFailure();
	}

	/// <inheritdoc />
	public void SetBounds(WindowsPreviewBounds bounds)
	{
		EnsureActive();
		var rectangle = ToRect(bounds);
		_handler.SetRect(in rectangle).ThrowOnFailure();
	}

	/// <inheritdoc />
	public void SetTheme(WindowsPreviewColor background, WindowsPreviewColor foreground)
	{
		EnsureActive();
		var visuals = _handler as IPreviewHandlerVisuals;
		if (visuals is null)
		{
			return;
		}

		var hr = visuals.SetBackgroundColor((COLORREF)ToColorRef(background));
		hr.ThrowOnFailure();
		hr = visuals.SetTextColor((COLORREF)ToColorRef(foreground));
		hr.ThrowOnFailure();
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
	public HWND QueryFocus()
	{
		EnsureActive();
		_handler.QueryFocus(out var focus).ThrowOnFailure();

		return focus;
	}

	/// <inheritdoc />
	public bool TryTranslateAccelerator(in MSG message)
	{
		EnsureActive();
		var hr = _handler.TranslateAccelerator(in message);
		if (hr == HRESULT.S_FALSE)
		{
			return false;
		}

		hr.ThrowOnFailure();

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
			_initializedStream = null;
			_initializedItem = null;
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
			var siteInterface = _handler as IObjectWithSite;
			if (siteInterface is not null)
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

	private static bool IsOptionalInitializationFailure(HRESULT hr)
	{
		return hr == HRESULT.E_NOINTERFACE || hr == HRESULT.E_NOTIMPL;
	}

	private static RECT ToRect(WindowsPreviewBounds bounds)
	{
		RECT rectangle = default;
		rectangle.left = bounds.X;
		rectangle.top = bounds.Y;
		rectangle.right = checked(bounds.X + bounds.Width);
		rectangle.bottom = checked(bounds.Y + bounds.Height);

		return rectangle;
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

}
