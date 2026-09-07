// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;
using ShellServiceProvider = Windows.Win32.System.Com.IServiceProvider;

namespace Files.Core.Windows;

/// <summary>
/// Registers one top-level ReFiles window with the Windows Shell window collection.
/// </summary>
/// <remarks>
/// The registration is window-scoped. The facade always exposes the active pane of the
/// window, so ReFiles can keep multiple tabs without publishing a private tab-enumeration
/// interface. Locations that do not have a Windows Shell view are reported as unsupported.
/// </remarks>
[SupportedOSPlatform("windows6.0.6000")]
public sealed unsafe partial class WindowsShellBrowserRegistration : IAsyncDisposable
{
	private readonly WindowsShellBrowserViewHost _viewHost;
	private readonly ShellBrowserFacade _facade;
	private readonly IShellWindows _shellWindows;
	private readonly int _cookie;
	private int _isDisposed;

	/// <summary>Initializes a Shell browser registration for one top-level window.</summary>
	/// <param name="windowHandle">The top-level ReFiles window handle.</param>
	/// <exception cref="ArgumentException">Thrown when the window handle is null.</exception>
	/// <exception cref="COMException">Thrown when the Shell window collection cannot be registered.</exception>
	public WindowsShellBrowserRegistration(HWND windowHandle)
	{
		if (windowHandle.IsNull)
		{
			throw new ArgumentException("The Shell browser window handle cannot be null.", nameof(windowHandle));
		}

		_viewHost = WindowsShellBrowserViewHost.Create(windowHandle) ?? throw new InvalidOperationException("The Shell browser view could not be created.");
		try
		{
			_facade = new ShellBrowserFacade(windowHandle, _viewHost, this);
			_shellWindows = ShellWindows.CreateInstance<IShellWindows>();
			var hr = _shellWindows.Register(_facade, checked((int)(nint)windowHandle), ShellWindowTypeConstants.SWC_BROWSER, out _cookie);
			if (hr.Failed)
			{
				hr.ThrowOnFailure();
			}
		}
		catch
		{
			_viewHost.DisposeAsync().AsTask().GetAwaiter().GetResult();

			throw;
		}
	}

	/// <summary>Updates the active Shell folder represented by the registration.</summary>
	/// <param name="parsingName">The absolute Shell parsing name, or <see langword="null"/> for a non-Shell location.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>A value indicating whether the location was accepted by ExplorerBrowser.</returns>
	public bool UpdateLocation(string? parsingName, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		if (string.IsNullOrWhiteSpace(parsingName))
		{
			_facade.SetLocation(null);
			_viewHost.Clear();

			return false;
		}

		_facade.SetLocation(null);
		_viewHost.Clear();
		var accepted = _viewHost.Navigate(parsingName, cancellationToken);
		if (accepted)
		{
			_facade.SetLocation(parsingName);
			if (TryCreatePidlVariant(parsingName, out var location))
			{
				try
				{
					_shellWindows.OnNavigate(_cookie, in location).ThrowOnFailure();
				}
				finally
				{
					location.Dispose();
				}
			}
		}

		return accepted;
	}

	/// <summary>Updates the selected child items exposed by the registered Shell view.</summary>
	/// <param name="parsingNames">Absolute Shell parsing names for the selected children.</param>
	public void UpdateSelection(IEnumerable<string> parsingNames)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		ArgumentNullException.ThrowIfNull(parsingNames);

		_viewHost.SetSelection(parsingNames);
	}

	internal HRESULT NavigateFromShell(ITEMIDLIST* pidl, uint flags)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		if (pidl is null)
		{
			return HRESULT.E_POINTER;
		}

		if (!_viewHost.Navigate(pidl, flags) || !TryGetParsingName(pidl, out var parsingName))
		{
			return HRESULT.E_FAIL;
		}

		_facade.SetLocation(parsingName);
		if (TryCreatePidlVariant(pidl, out var location))
		{
			try
			{
				_shellWindows.OnNavigate(_cookie, in location).ThrowOnFailure();
			}
			finally
			{
				location.Dispose();
			}
		}

		return HRESULT.S_OK;
	}

	/// <summary>Revokes the Shell window registration and releases its hidden view.</summary>
	/// <returns>A value task that represents disposal.</returns>
	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is 0)
		{
			_shellWindows.Revoke(_cookie);

			return _viewHost.DisposeAsync();
		}

		GC.SuppressFinalize(this);

		return ValueTask.CompletedTask;
	}

	private static bool TryCreatePidlVariant(string parsingName, out ComVariant variant)
	{
		variant = default;
		ITEMIDLIST* pidl = null;
		try
		{
			var hr = PInvoke.SHParseDisplayName(parsingName, null, out pidl, 0, out _);
			if (hr.Failed || pidl is null)
			{
				return false;
			}

			hr = PInvoke.InitVariantFromBuffer(pidl, checked((uint)GetPidlSize(pidl)), out variant);
			if (hr.Failed)
			{
				variant.Dispose();
				variant = default;

				return false;
			}

			return true;
		}
		finally
		{
			if (pidl is not null)
			{
				PInvoke.CoTaskMemFree(pidl);
			}
		}
	}

	private static bool TryCreatePidlVariant(ITEMIDLIST* pidl, out ComVariant variant)
	{
		variant = default;
		if (pidl is null)
		{
			return false;
		}

		var hr = PInvoke.InitVariantFromBuffer(pidl, checked((uint)GetPidlSize(pidl)), out variant);
		if (hr.Failed)
		{
			variant.Dispose();
			variant = default;

			return false;
		}

		return true;
	}

	private static bool TryGetParsingName(ITEMIDLIST* pidl, out string parsingName)
	{
		parsingName = string.Empty;
		if (pidl is null)
		{
			return false;
		}

		var hr = PInvoke.SHCreateItemFromIDList(in *pidl, out IShellItem shellItem);
		if (hr.Failed)
		{
			return false;
		}

		hr = shellItem.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, out var displayName);
		if (hr.Failed)
		{
			return false;
		}

		try
		{
			parsingName = displayName.ToString();

			return !string.IsNullOrWhiteSpace(parsingName);
		}
		finally
		{
			PInvoke.CoTaskMemFree(displayName.Value);
		}
	}

	private static int GetPidlSize(ITEMIDLIST* pidl)
	{
		var cursor = (byte*)pidl;
		var size = 0;
		while (true)
		{
			var itemSize = *(ushort*)cursor;
			size = checked(size + itemSize);
			if (itemSize is 0)
			{
				return size;
			}

			cursor += itemSize;
		}
	}

	[GeneratedComClass]
	private sealed partial class ShellBrowserFacade : IWebBrowserApp, ShellServiceProvider, IShellBrowser
	{
		private readonly HWND _windowHandle;
		private readonly WindowsShellBrowserViewHost _viewHost;
		private readonly WindowsShellBrowserRegistration _registration;
		private string? _location;

		internal ShellBrowserFacade(HWND windowHandle, WindowsShellBrowserViewHost viewHost, WindowsShellBrowserRegistration registration)
		{
			_windowHandle = windowHandle;
			_viewHost = viewHost;
			_registration = registration;
		}

		internal void SetLocation(string? parsingName)
		{
			_location = parsingName;
		}

		public void __IDispatchPlaceholder1()
		{
		}

		public void __IDispatchPlaceholder2()
		{
		}

		public void __IDispatchPlaceholder3()
		{
		}

		public void __IDispatchPlaceholder4()
		{
		}

		public HRESULT GoBack() => HRESULT.E_NOTIMPL;

		public HRESULT GoForward() => HRESULT.E_NOTIMPL;

		public HRESULT GoHome() => HRESULT.E_NOTIMPL;

		public HRESULT GoSearch() => HRESULT.E_NOTIMPL;

		public HRESULT Navigate(BSTR URL, ComVariant* Flags, ComVariant* TargetFrameName, ComVariant* PostData, ComVariant* Headers)
		{
			var location = URL.ToString();
			if (string.IsNullOrWhiteSpace(location))
			{
				return HRESULT.E_INVALIDARG;
			}

			return _registration.UpdateLocation(location) ? HRESULT.S_OK : HRESULT.E_FAIL;
		}

		public HRESULT Refresh() => HRESULT.E_NOTIMPL;

		public HRESULT Refresh2(ComVariant* Level) => HRESULT.E_NOTIMPL;

		public HRESULT Stop() => HRESULT.E_NOTIMPL;

		public HRESULT get_Application(out IDispatch ppDisp)
		{
			ppDisp = this;

			return HRESULT.S_OK;
		}

		public HRESULT get_Parent(out IDispatch ppDisp)
		{
			ppDisp = this;

			return HRESULT.S_OK;
		}

		public HRESULT get_Container(out IDispatch ppDisp)
		{
			ppDisp = this;

			return HRESULT.S_OK;
		}

		public HRESULT get_Document(out IDispatch ppDisp)
		{
			ppDisp = this;

			return HRESULT.S_OK;
		}

		public HRESULT get_TopLevelContainer(VARIANT_BOOL* pBool)
		{
			if (pBool is null)
			{
				return HRESULT.E_POINTER;
			}

			*pBool = VARIANT_BOOL.VARIANT_TRUE;

			return HRESULT.S_OK;
		}

		public HRESULT get_Type(BSTR* Type) => ENotImplemented(Type);

		public HRESULT get_Left(out int pl) => GetZero(out pl);

		public HRESULT put_Left(int Left) => HRESULT.E_NOTIMPL;

		public HRESULT get_Top(out int pl) => GetZero(out pl);

		public HRESULT put_Top(int Top) => HRESULT.E_NOTIMPL;

		public HRESULT get_Width(out int pl) => GetZero(out pl);

		public HRESULT put_Width(int Width) => HRESULT.E_NOTIMPL;

		public HRESULT get_Height(out int pl) => GetZero(out pl);

		public HRESULT put_Height(int Height) => HRESULT.E_NOTIMPL;

		public HRESULT get_LocationName(BSTR* LocationName) => WriteBstr(LocationName, _location);

		public HRESULT get_LocationURL(BSTR* LocationURL) => WriteBstr(LocationURL, _location);

		public HRESULT get_Busy(VARIANT_BOOL* pBool)
		{
			if (pBool is null)
			{
				return HRESULT.E_POINTER;
			}

			*pBool = VARIANT_BOOL.VARIANT_FALSE;

			return HRESULT.S_OK;
		}

		public HRESULT Quit() => HRESULT.E_NOTIMPL;

		public HRESULT ClientToWindow(ref int pcx, ref int pcy) => HRESULT.E_NOTIMPL;

		public HRESULT PutProperty(BSTR Property, ComVariant vtValue) => HRESULT.E_NOTIMPL;

		public HRESULT GetProperty(BSTR Property, out ComVariant pvtValue)
		{
			pvtValue = default;

			return HRESULT.E_NOTIMPL;
		}

		public HRESULT get_Name(BSTR* Name) => WriteBstr(Name, "ReFiles");

		public HRESULT get_HWND(SHANDLE_PTR* pHWND)
		{
			if (pHWND is null)
			{
				return HRESULT.E_POINTER;
			}

			*pHWND = (SHANDLE_PTR)(nint)_windowHandle;

			return HRESULT.S_OK;
		}

		public HRESULT get_FullName(BSTR* FullName) => ENotImplemented(FullName);

		public HRESULT get_Path(BSTR* Path) => ENotImplemented(Path);

		public HRESULT get_Visible(VARIANT_BOOL* pBool)
		{
			if (pBool is null)
			{
				return HRESULT.E_POINTER;
			}

			*pBool = VARIANT_BOOL.VARIANT_TRUE;

			return HRESULT.S_OK;
		}

		public HRESULT put_Visible(VARIANT_BOOL Value) => HRESULT.E_NOTIMPL;

		public HRESULT get_StatusBar(VARIANT_BOOL* pBool) => ENotImplemented(pBool);

		public HRESULT put_StatusBar(VARIANT_BOOL Value) => HRESULT.E_NOTIMPL;

		public HRESULT get_StatusText(BSTR* StatusText) => ENotImplemented(StatusText);

		public HRESULT put_StatusText(BSTR StatusText) => HRESULT.E_NOTIMPL;

		public HRESULT get_ToolBar(out int Value) => GetZero(out Value);

		public HRESULT put_ToolBar(int Value) => HRESULT.E_NOTIMPL;

		public HRESULT get_MenuBar(VARIANT_BOOL* Value) => ENotImplemented(Value);

		public HRESULT put_MenuBar(VARIANT_BOOL Value) => HRESULT.E_NOTIMPL;

		public HRESULT get_FullScreen(VARIANT_BOOL* pbFullScreen) => ENotImplemented(pbFullScreen);

		public HRESULT put_FullScreen(VARIANT_BOOL bFullScreen) => HRESULT.E_NOTIMPL;

		public HRESULT QueryService(Guid* guidService, Guid* riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject)
		{
			ppvObject = null!;
			if (riid is null)
			{
				return HRESULT.E_POINTER;
			}

			if (*riid == typeof(IShellBrowser).GUID)
			{
				ppvObject = this;

				return HRESULT.S_OK;
			}

			if (*riid == typeof(IShellView).GUID && _viewHost.ActiveView is { } view)
			{
				ppvObject = view;

				return HRESULT.S_OK;
			}

			return HRESULT.E_NOINTERFACE;
		}

		public unsafe HRESULT InsertMenusSB(HMENU hmenuShared, OLEMENUGROUPWIDTHS* lpMenuWidths) => HRESULT.E_NOTIMPL;

		public HRESULT SetMenuSB(HMENU hmenuShared, nint holemenuRes, HWND hwndActiveObject) => HRESULT.E_NOTIMPL;

		public HRESULT RemoveMenusSB(HMENU hmenuShared) => HRESULT.E_NOTIMPL;

		public HRESULT SetStatusTextSB(PCWSTR pszStatusText) => HRESULT.E_NOTIMPL;

		public HRESULT EnableModelessSB(BOOL fEnable) => HRESULT.E_NOTIMPL;

		public unsafe HRESULT TranslateAcceleratorSB(MSG* pmsg, ushort wID) => HRESULT.E_NOTIMPL;

		public unsafe HRESULT BrowseObject(ITEMIDLIST* pidl, uint wFlags) => _registration.NavigateFromShell(pidl, wFlags);

		public HRESULT GetViewStateStream(uint grfMode, out IStream ppStrm)
		{
			ppStrm = null!;

			return HRESULT.E_NOTIMPL;
		}

		public unsafe HRESULT GetControlWindow(uint id, HWND* phwnd) => ENotImplemented(phwnd);

		public unsafe HRESULT SendControlMsg(uint id, uint uMsg, WPARAM wParam, LPARAM lParam, LRESULT* pret) => ENotImplemented(pret);

		public HRESULT QueryActiveShellView(out IShellView ppshv)
		{
			ppshv = _viewHost.ActiveView!;

			return ppshv is null ? HRESULT.E_NOINTERFACE : HRESULT.S_OK;
		}

		public HRESULT OnViewWindowActive(IShellView pshv) => HRESULT.S_OK;

		public unsafe HRESULT SetToolbarItems(TBBUTTON* lpButtons, uint nButtons, uint uFlags) => HRESULT.E_NOTIMPL;

		public unsafe HRESULT GetWindow(HWND* phwnd)
		{
			if (phwnd is null)
			{
				return HRESULT.E_POINTER;
			}

			*phwnd = _windowHandle;

			return HRESULT.S_OK;
		}

		public HRESULT ContextSensitiveHelp(BOOL fEnterMode) => HRESULT.E_NOTIMPL;

		private static HRESULT GetZero(out int value)
		{
			value = 0;

			return HRESULT.S_OK;
		}

		private static unsafe HRESULT WriteBstr(BSTR* output, string? value)
		{
			if (output is null)
			{
				return HRESULT.E_POINTER;
			}

			*output = value is null ? default : (BSTR)(char*)Marshal.StringToBSTR(value);

			return HRESULT.S_OK;
		}

		private static unsafe HRESULT ENotImplemented<T>(T* output) where T : unmanaged
		{
			if (output is not null)
			{
				*output = default;
			}

			return HRESULT.E_NOTIMPL;
		}

		private static HRESULT ENotImplemented<T>(T output) where T : class
		{
			return HRESULT.E_NOTIMPL;
		}
	}
}
