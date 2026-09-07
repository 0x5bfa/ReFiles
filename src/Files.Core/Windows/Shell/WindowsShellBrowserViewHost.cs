// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Windows;

/// <summary>
/// Owns a hidden ExplorerBrowser view used as the Shell-compatible view for one ReFiles window.
/// </summary>
internal sealed unsafe partial class WindowsShellBrowserViewHost : IAsyncDisposable
{
	private readonly HWND _hostWindow;
	private readonly IExplorerBrowser _browser;
	private readonly ExplorerBrowserEvents _events;
	private readonly uint _eventCookie;
	private readonly Lock _stateLock = new();

	private IShellView? _activeView;
	private IReadOnlyList<string> _pendingSelection = [];
	private int _isDisposed;

	internal IShellView? ActiveView
	{
		get
		{
			lock (_stateLock)
			{
				return _activeView;
			}
		}
	}

	private WindowsShellBrowserViewHost(HWND hostWindow, IExplorerBrowser browser, ExplorerBrowserEvents events, uint eventCookie)
	{
		_hostWindow = hostWindow;
		_browser = browser;
		_events = events;
		_eventCookie = eventCookie;
	}

	internal static WindowsShellBrowserViewHost? Create(HWND parentWindow)
	{
		if (parentWindow.IsNull)
		{
			throw new ArgumentException("The Shell browser parent window cannot be null.", nameof(parentWindow));
		}

		var hostWindow = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, "STATIC", null, WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_DISABLED,
			0, 0, 1, 1, parentWindow, null, null, null);
		if (hostWindow.IsNull)
		{
			return null;
		}

		IExplorerBrowser? browser = null;
		try
		{
			browser = ExplorerBrowser.CreateInstance<IExplorerBrowser>();
			var rect = default(RECT);
			var hr = browser.Initialize(hostWindow, &rect, null);
			if (hr.Failed || browser.SetPropertyBag(WindowsShellViewStateBridge.ShellPropertyBagName).Failed)
			{
				return null;
			}

			browser.SetOptions(EXPLORER_BROWSER_OPTIONS.EBO_NOBORDER | EXPLORER_BROWSER_OPTIONS.EBO_NOTRAVELLOG).ThrowOnFailure();
			var events = new ExplorerBrowserEvents();
			hr = browser.Advise(events, out var eventCookie);
			if (hr.Failed)
			{
				return null;
			}

			var host = new WindowsShellBrowserViewHost(hostWindow, browser, events, eventCookie);
			events.Attach(host);
			browser = null;

			return host;
		}
		finally
		{
			if (browser is not null)
			{
				browser.Destroy();
			}

			if (!hostWindow.IsNull && browser is not null)
			{
				PInvoke.DestroyWindow(hostWindow);
			}
		}
	}

	internal bool Navigate(string parsingName, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		cancellationToken.ThrowIfCancellationRequested();

		ITEMIDLIST* pidl = null;
		SetActiveView(null);
		var hr = PInvoke.SHParseDisplayName(parsingName, null, out pidl, 0, out _);
		if (hr.Failed || pidl is null)
		{
			if (pidl is not null)
			{
				PInvoke.CoTaskMemFree(pidl);
			}

			return false;
		}

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			hr = _browser.BrowseToIDList(in *pidl, 0);

			return hr.Succeeded;
		}
		finally
		{
			PInvoke.CoTaskMemFree(pidl);
		}
	}

	internal bool Navigate(ITEMIDLIST* pidl, uint flags)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		if (pidl is null)
		{
			return false;
		}

		SetActiveView(null);
		var hr = _browser.BrowseToIDList(in *pidl, flags);

		return hr.Succeeded;
	}

	internal void Clear()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		lock (_stateLock)
		{
			_activeView = null;
			_pendingSelection = [];
		}
	}

	internal void SetSelection(IEnumerable<string> parsingNames)
	{
		ArgumentNullException.ThrowIfNull(parsingNames);

		var selection = Array.AsReadOnly(parsingNames.Where(static parsingName => !string.IsNullOrWhiteSpace(parsingName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
		IShellView? activeView;
		lock (_stateLock)
		{
			_pendingSelection = selection;
			activeView = _activeView;
		}

		if (activeView is not null)
		{
			ApplySelection(activeView, selection);
		}
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is 0)
		{
			_browser.Unadvise(_eventCookie);
			_browser.Destroy();
			PInvoke.DestroyWindow(_hostWindow);
		}

		GC.SuppressFinalize(this);

		return ValueTask.CompletedTask;
	}

	private void SetActiveView(IShellView? view)
	{
		IReadOnlyList<string> selection;
		lock (_stateLock)
		{
			_activeView = view;
			selection = _pendingSelection;
		}

		if (view is not null)
		{
			ApplySelection(view, selection);
		}
	}

	private static void ApplySelection(IShellView view, IReadOnlyList<string> parsingNames)
	{
		if (parsingNames.Count is 0)
		{
			view.SelectItem(null, (uint)_SVSIF.SVSI_DESELECTOTHERS);

			return;
		}

		var shellFolderId = typeof(IShellFolder).GUID;
		var firstItem = true;
		foreach (var parsingName in parsingNames)
		{
			ITEMIDLIST* absolutePidl = null;
			var hr = PInvoke.SHParseDisplayName(parsingName, null, out absolutePidl, 0, out _);
			if (hr.Failed || absolutePidl is null)
			{
				if (absolutePidl is not null)
				{
					PInvoke.CoTaskMemFree(absolutePidl);
				}

				continue;
			}

			try
			{
				hr = PInvoke.SHBindToParent(in *absolutePidl, in shellFolderId, out object parentObject, out ITEMIDLIST* childPidl);
				if (hr.Failed || parentObject is not IShellFolder || childPidl is null)
				{
					continue;
				}

				var flags = (uint)_SVSIF.SVSI_SELECT | (uint)_SVSIF.SVSI_ENSUREVISIBLE;
				if (firstItem)
				{
					flags |= (uint)_SVSIF.SVSI_DESELECTOTHERS | (uint)_SVSIF.SVSI_FOCUSED;
				}

				if (view.SelectItem(childPidl, flags).Succeeded)
				{
					firstItem = false;
				}
			}
			finally
			{
				PInvoke.CoTaskMemFree(absolutePidl);
			}
		}
	}

	[GeneratedComClass]
	private sealed partial class ExplorerBrowserEvents : IExplorerBrowserEvents
	{
		private WindowsShellBrowserViewHost? _owner;

		internal void Attach(WindowsShellBrowserViewHost owner)
		{
			_owner = owner;
		}

		public HRESULT OnNavigationPending(ITEMIDLIST* pidlFolder)
		{
			_owner?.SetActiveView(null);

			return HRESULT.S_OK;
		}

		public HRESULT OnViewCreated(IShellView psv)
		{
			_owner?.SetActiveView(psv);

			return HRESULT.S_OK;
		}

		public HRESULT OnNavigationComplete(ITEMIDLIST* pidlFolder)
		{
			if (_owner is { } owner && owner._browser.GetCurrentView<IShellView>(out var view).Succeeded)
			{
				owner.SetActiveView(view);
			}

			return HRESULT.S_OK;
		}

		public HRESULT OnNavigationFailed(ITEMIDLIST* pidlFolder)
		{
			_owner?.SetActiveView(null);

			return HRESULT.S_OK;
		}
	}
}
