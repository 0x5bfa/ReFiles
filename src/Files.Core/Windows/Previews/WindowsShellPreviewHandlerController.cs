// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.PropertiesSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Windows;

[SupportedOSPlatform("windows6.0.6000")]
internal sealed class WindowsShellPreviewHandlerController : IWindowsPreviewHandlerController
{
	private const int ServerExecutionFailure = unchecked((int)0x80080005);
	private const int ActivationAttemptCount = 3;
	private const int ActivationRetryDelayMilliseconds = 250;

	private IPreviewHandler? _handler;
	private IStream? _initializedStream;
	private IShellItem? _initializedItem;
	private WindowsPreviewHandlerFrame? _previewHandlerFrame;
	private bool _isInitialized;
	private bool _isSiteSet;
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
		var handler = Activate(handlerClsid, activationContext);

		return new WindowsShellPreviewHandlerController(handler);
	}

	/// <inheritdoc />
	public void SetSite()
	{
		SetSite(HWND.Null, null);
	}

	/// <inheritdoc />
	public void SetSite(HWND hostWindow, WindowsPreviewAcceleratorForwarder? acceleratorForwarder)
	{
		EnsureActive();
		var siteInterface = _handler as IObjectWithSite;
		if (siteInterface is null)
		{
			return;
		}

		var frame = new WindowsPreviewHandlerFrame(hostWindow, acceleratorForwarder);
		_previewHandlerFrame = frame;
		try
		{
			siteInterface.SetSite(frame).ThrowOnFailure();
			_isSiteSet = true;
		}
		catch
		{
			_previewHandlerFrame = null;

			throw;
		}
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

		var hr = PInvoke.SHCreateStreamOnFileEx(fileSystemPath, (uint)(STGM.STGM_READ | STGM.STGM_SHARE_DENY_WRITE), 0, false, null!, out IStream stream);
		if (hr.Failed)
		{
			return false;
		}

		hr = initializer.Initialize(stream, (uint)STGM.STGM_READ);
		if (hr.Failed)
		{
			return false;
		}

		_initializedStream = stream;
		_isInitialized = true;

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
		if (hr.Failed)
		{
			return false;
		}

		hr = initializer.Initialize(item, (uint)STGM.STGM_READ);
		if (hr.Failed)
		{
			return false;
		}

		_initializedItem = item;
		_isInitialized = true;

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

		_isInitialized = hr.Succeeded;

		return _isInitialized;
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
	public unsafe void ApplySystemVisuals()
	{
		EnsureActive();
		var visuals = _handler as IPreviewHandlerVisuals;
		if (visuals is null)
		{
			return;
		}

		_ = visuals.SetBackgroundColor((COLORREF)PInvoke.GetSysColor(SYS_COLOR_INDEX.COLOR_WINDOW));
		_ = visuals.SetTextColor((COLORREF)PInvoke.GetSysColor(SYS_COLOR_INDEX.COLOR_WINDOWTEXT));

		NONCLIENTMETRICSW metrics = default;
		metrics.cbSize = (uint)sizeof(NONCLIENTMETRICSW);
		if (PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETNONCLIENTMETRICS, metrics.cbSize, &metrics, 0))
		{
			_ = visuals.SetFont(in metrics.lfMessageFont);
		}
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
			if (_isInitialized && !_didUnload)
			{
				TryCleanup(() => handler.Unload().ThrowOnFailure(), errors);
				_didUnload = true;
			}

			if (_isSiteSet)
			{
				TryCleanup(SetSiteForCleanup, errors);
			}

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

	private static IPreviewHandler Activate(Guid handlerClsid, uint activationContext)
	{
		for (var attempt = 1; attempt <= ActivationAttemptCount; attempt++)
		{
			var hr = PInvoke.CoCreateInstance(in handlerClsid, null, (CLSCTX)activationContext, out IPreviewHandler handler);
			if (hr.Succeeded && handler is not null)
			{
				return handler;
			}

			if (hr.Value != ServerExecutionFailure || attempt == ActivationAttemptCount)
			{
				throw new COMException("The Windows preview handler could not be activated.", hr.Value);
			}

			Thread.Sleep(ActivationRetryDelayMilliseconds * attempt);
		}

		throw new InvalidOperationException("The Windows preview handler activation retry loop ended unexpectedly.");
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
			_isSiteSet = false;
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

/// <summary>Creates controllers for Windows Shell preview handlers.</summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsShellPreviewHandlerControllerFactory : IWindowsPreviewHandlerControllerFactory
{
	private readonly IWindowsPreviewHandlerActivationPolicy _activationPolicy;

	/// <summary>Initializes a controller factory with the local-server policy.</summary>
	public WindowsShellPreviewHandlerControllerFactory() : this(new LocalServerWindowsPreviewHandlerActivationPolicy())
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
		const WindowsPreviewHandlerActivationContext serverKinds = WindowsPreviewHandlerActivationContext.InProcessServer | WindowsPreviewHandlerActivationContext.LocalServer;
		const WindowsPreviewHandlerActivationContext supportedFlags = serverKinds | WindowsPreviewHandlerActivationContext.EnableCloaking;
		if ((activationContext & serverKinds) == 0 || (activationContext & ~supportedFlags) != 0)
		{
			throw new InvalidOperationException("Preview handlers must be activated through an in-process or local server COM class.");
		}

		return WindowsShellPreviewHandlerController.Create(handlerClsid, (uint)activationContext);
	}
}

/// <summary>Minimal in-process COM site exposed to a preview handler.</summary>
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
