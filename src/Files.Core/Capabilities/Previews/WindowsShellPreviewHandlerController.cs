// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Security;
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
	private readonly IWindowsPreviewHandlerIsolationPolicy _isolationPolicy;

	/// <summary>Initializes a controller factory with the local-server policy.</summary>
	public WindowsShellPreviewHandlerControllerFactory()
		: this(new LocalServerWindowsPreviewHandlerActivationPolicy(), new WindowsPreviewHandlerIsolationPolicy())
	{
	}

	/// <summary>Initializes a controller factory.</summary>
	/// <param name="activationPolicy">The activation policy.</param>
	public WindowsShellPreviewHandlerControllerFactory(IWindowsPreviewHandlerActivationPolicy activationPolicy)
		: this(activationPolicy, new WindowsPreviewHandlerIsolationPolicy())
	{
	}

	internal WindowsShellPreviewHandlerControllerFactory(IWindowsPreviewHandlerActivationPolicy activationPolicy, IWindowsPreviewHandlerIsolationPolicy isolationPolicy)
	{
		ArgumentNullException.ThrowIfNull(activationPolicy);
		ArgumentNullException.ThrowIfNull(isolationPolicy);

		_activationPolicy = activationPolicy;
		_isolationPolicy = isolationPolicy;
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
		var requiredContext = WindowsPreviewHandlerActivationContext.LocalServer | WindowsPreviewHandlerActivationContext.EnableCloaking;
		if (activationContext != requiredContext)
		{
			throw new InvalidOperationException("Preview handlers must be activated through a cloaked local server.");
		}

		var useLowIntegrity = _isolationPolicy.RequiresLowIntegrity(handlerClsid);
		var nativeContext = useLowIntegrity ? activationContext : WindowsPreviewHandlerActivationContext.LocalServer;

		return WindowsShellPreviewHandlerController.Create(handlerClsid, (uint)nativeContext, useLowIntegrity);
	}
}

internal interface IWindowsPreviewHandlerIsolationPolicy
{
	bool RequiresLowIntegrity(Guid handlerClsid);
}

internal sealed class WindowsPreviewHandlerIsolationPolicy : IWindowsPreviewHandlerIsolationPolicy
{
	private const string DisableLowIntegrityValue = "DisableLowILProcessIsolation";

	public bool RequiresLowIntegrity(Guid handlerClsid)
	{
		var keyPath = $"Software\\Classes\\CLSID\\{handlerClsid:B}";
		if (TryReadOptOut(RegistryView.Registry64, keyPath, out var disabled) || TryReadOptOut(RegistryView.Registry32, keyPath, out disabled))
		{
			return !disabled;
		}

		return true;
	}

	internal static bool IsLowIntegrityDisabled(object? value, RegistryValueKind valueKind)
	{
		if (valueKind is RegistryValueKind.DWord && value is int numericValue)
		{
			return numericValue is not 0;
		}

		return valueKind is RegistryValueKind.Binary && value is byte[] { Length: sizeof(uint) } bytes && BinaryPrimitives.ReadUInt32LittleEndian(bytes) is not 0;
	}

	private static bool TryReadOptOut(RegistryView view, string keyPath, out bool disabled)
	{
		disabled = false;
		RegistryKey? key;
		try
		{
			using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
			key = localMachine.OpenSubKey(keyPath, writable: false);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
		{
			return false;
		}

		if (key is null)
		{
			return false;
		}

		using (key)
		{
			try
			{
				var value = key.GetValue(DisableLowIntegrityValue);
				disabled = value is not null && IsLowIntegrityDisabled(value, key.GetValueKind(DisableLowIntegrityValue));
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
			{
				disabled = false;
			}
		}

		return true;
	}
}

[SupportedOSPlatform("windows6.0.6000")]
internal sealed class WindowsShellPreviewHandlerController : IWindowsPreviewHandlerController
{
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
	/// <param name="useLowIntegrity">Whether to activate the handler under a low-integrity impersonation token.</param>
	/// <returns>The created controller.</returns>
	public static WindowsShellPreviewHandlerController Create(Guid handlerClsid, uint activationContext, bool useLowIntegrity)
	{
		var handler = useLowIntegrity ? ActivateWithLowIntegrity(handlerClsid, activationContext) : Activate(handlerClsid, activationContext);

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
		var hr = PInvoke.CoCreateInstance(in handlerClsid, null, (CLSCTX)activationContext, out IPreviewHandler handler);
		if (hr.Failed || handler is null)
		{
			throw new COMException("The Windows preview handler could not be activated.", hr.Value);
		}

		return handler;
	}

	private static unsafe IPreviewHandler ActivateWithLowIntegrity(Guid handlerClsid, uint activationContext)
	{
		if (!PInvoke.ConvertStringSidToSid("LW", out var lowIntegritySid))
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "The low-integrity SID could not be created.");
		}

		try
		{
			if (!PInvoke.ImpersonateSelf(SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation))
			{
				throw new Win32Exception(Marshal.GetLastPInvokeError(), "The preview thread could not impersonate itself.");
			}

			IPreviewHandler? handler = null;
			Exception? activationError = null;
			try
			{
				using var process = PInvoke.GetCurrentProcess_SafeHandle();
				if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_DUPLICATE, out var processToken))
				{
					throw new Win32Exception(Marshal.GetLastPInvokeError(), "The preview process token could not be opened.");
				}

				using (processToken)
				{
					var desiredAccess = TOKEN_ACCESS_MASK.TOKEN_ADJUST_DEFAULT | TOKEN_ACCESS_MASK.TOKEN_IMPERSONATE;
					if (!PInvoke.DuplicateTokenEx(processToken, desiredAccess, null, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenImpersonation, out var lowIntegrityToken))
					{
						throw new Win32Exception(Marshal.GetLastPInvokeError(), "The low-integrity preview token could not be created.");
					}

					using (lowIntegrityToken)
					{
						SetLowIntegrity(lowIntegrityToken, lowIntegritySid);
						if (!PInvoke.SetThreadToken(PInvoke.GetCurrentThread(), lowIntegrityToken))
						{
							throw new Win32Exception(Marshal.GetLastPInvokeError(), "The low-integrity preview token could not be assigned.");
						}
					}
				}

				handler = Activate(handlerClsid, activationContext);
			}
			catch (Exception error)
			{
				activationError = error;
			}

			if (!PInvoke.RevertToSelf())
			{
				var error = new Win32Exception(Marshal.GetLastPInvokeError(), "The preview thread identity could not be restored.");
				Environment.FailFast("The preview thread could not safely leave low-integrity impersonation.", error);
			}

			if (activationError is not null)
			{
				ExceptionDispatchInfo.Capture(activationError).Throw();
			}

			return handler ?? throw new InvalidOperationException("The preview handler activation returned no handler.");
		}
		finally
		{
			PInvoke.LocalFree((HLOCAL)(nint)lowIntegritySid.Value);
		}
	}

	private static unsafe void SetLowIntegrity(SafeHandle token, PSID lowIntegritySid)
	{
		var sidLength = checked((int)PInvoke.GetLengthSid(lowIntegritySid));
		var informationLength = checked(sizeof(TOKEN_MANDATORY_LABEL) + sidLength);
		Span<byte> information = stackalloc byte[informationLength];
		new ReadOnlySpan<byte>(lowIntegritySid.Value, sidLength).CopyTo(information[sizeof(TOKEN_MANDATORY_LABEL)..]);
		fixed (byte* informationPointer = information)
		{
			TOKEN_MANDATORY_LABEL mandatoryLabel = default;
			mandatoryLabel.Label.Sid = new PSID(informationPointer + sizeof(TOKEN_MANDATORY_LABEL));
			mandatoryLabel.Label.Attributes = PInvoke.SE_GROUP_INTEGRITY;
			*(TOKEN_MANDATORY_LABEL*)informationPointer = mandatoryLabel;
		}

		if (!PInvoke.SetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenIntegrityLevel, information))
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "The preview thread integrity level could not be lowered.");
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
