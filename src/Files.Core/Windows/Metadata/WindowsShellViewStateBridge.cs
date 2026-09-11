// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Files.Core.ViewSettings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Windows;

/// <summary>
/// Reads and writes the Windows Shell view state through an ExplorerBrowser configured with the Shell property bag.
/// </summary>
/// <remarks>
/// The <c>Shell</c> property-bag name is the name used by ExplorerBrowser for the shared Explorer view store. The
/// bridge deliberately uses the documented ExplorerBrowser, IFolderView2, IColumnManager, and IShellView contracts
/// instead of decoding the Shell's private registry payload.
/// </remarks>
internal static unsafe partial class WindowsShellViewStateBridge
{
	internal const string ShellPropertyBagName = "Shell";

	private const uint BrowseFlags = 0;
	private const uint MaximumColumnCount = 1024;
	private const int DefaultImageSize = -1;

	internal static BrowseViewSettings? Read(IShellItem shellItem, string parsingName, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(shellItem);

		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		return Run(shellItem, parsingName, null, cancellationToken);
	}

	internal static BrowseViewSettings? Apply(IShellItem shellItem, string parsingName, BrowseViewSettingsOverride settingsOverride, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(shellItem);

		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		ArgumentNullException.ThrowIfNull(settingsOverride);

		return Run(shellItem, parsingName, settingsOverride, cancellationToken);
	}

	internal static bool IsGrouped(HRESULT result)
	{
		return result == HRESULT.S_OK;
	}

	private static BrowseViewSettings? Run(IShellItem shellItem, string parsingName, BrowseViewSettingsOverride? settingsOverride, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		ITEMIDLIST* absolutePidl = null;
		var hr = PInvoke.SHGetIDListFromObject(shellItem, out absolutePidl);
		if (hr.Failed || absolutePidl is null)
		{
			if (absolutePidl is not null)
			{
				PInvoke.CoTaskMemFree(absolutePidl);
			}

			return null;
		}

		var hostWindow = default(HWND);
		IExplorerBrowser? browser = null;
		uint adviseCookie = 0;
		try
		{
			hostWindow = CreateHostWindow();
			browser = ExplorerBrowser.CreateInstance<IExplorerBrowser>();
			var rect = default(RECT);
			hr = browser.Initialize(hostWindow, &rect, null);
			if (hr.Failed)
			{
				return null;
			}

			hr = browser.SetPropertyBag(ShellPropertyBagName);
			if (hr.Failed)
			{
				return null;
			}

			var events = new ExplorerBrowserEvents();
			hr = browser.Advise(events, out adviseCookie);
			if (hr.Failed)
			{
				return null;
			}

			cancellationToken.ThrowIfCancellationRequested();
			hr = browser.BrowseToIDList(in *absolutePidl, BrowseFlags);
			if (hr.Failed || !events.WaitForNavigation(TimeSpan.FromSeconds(5), cancellationToken))
			{
				return null;
			}

			if (browser.GetCurrentView<IFolderView2>(out var folderView).Failed || folderView is null)
			{
				return null;
			}

			if (folderView is not IShellView shellView)
			{
				return null;
			}

			if (folderView is not IColumnManager columnManager)
			{
				return null;
			}

			var state = ReadState(folderView, columnManager, hostWindow, cancellationToken);
			if (state is null)
			{
				return null;
			}

			if (settingsOverride is not null && !ApplyState(folderView, columnManager, state, settingsOverride, hostWindow, cancellationToken))
			{
				return null;
			}

			if (settingsOverride is not null)
			{
				shellView.SaveViewState().ThrowOnFailure();
				state = ReadState(folderView, columnManager, hostWindow, cancellationToken);
			}

			return state;
		}
		finally
		{
			if (browser is not null)
			{
				if (adviseCookie is not 0)
				{
					browser.Unadvise(adviseCookie);
				}

				browser.Destroy();
			}

			if (!hostWindow.IsNull)
			{
				PInvoke.DestroyWindow(hostWindow);
			}

			PInvoke.CoTaskMemFree(absolutePidl);
		}
	}

	private static HWND CreateHostWindow()
	{
		var window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, "STATIC", null, WINDOW_STYLE.WS_OVERLAPPED, 0, 0, 1, 1, HWND.Null, null, null, null);
		if (window.IsNull)
		{
			throw new InvalidOperationException("The Shell view host window could not be created.");
		}

		return window;
	}

	private static BrowseViewSettings? ReadState(IFolderView2 folderView, IColumnManager columnManager, HWND hostWindow, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (folderView.GetViewModeAndIconSize(out var viewMode, out _).Failed)
		{
			return null;
		}

		if (!TryGetColumnKeys(columnManager, CM_ENUM_FLAGS.CM_ENUM_ALL, cancellationToken, out var allKeys))
		{
			return null;
		}

		var visibleKeys = TryGetColumnKeys(columnManager, CM_ENUM_FLAGS.CM_ENUM_VISIBLE, cancellationToken, out var keys) ? keys : [];
		var visiblePropertyIds = visibleKeys.Select(WindowsShellColumnReader.GetPropertyId).ToHashSet(StringComparer.Ordinal);
		var columns = new List<ViewColumnSettings>(allKeys.Length);
		var dpi = PInvoke.GetDpiForWindow(hostWindow);
		if (dpi is 0)
		{
			dpi = 96;
		}

		for (var index = 0; index < allKeys.Length; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var propertyId = WindowsShellColumnReader.GetPropertyId(allKeys[index]);
			CM_COLUMNINFO columnInfo = default;
			columnInfo.cbSize = checked((uint)Marshal.SizeOf<CM_COLUMNINFO>());
			columnInfo.dwMask = (uint)(CM_MASK.CM_MASK_WIDTH | CM_MASK.CM_MASK_DEFAULTWIDTH | CM_MASK.CM_MASK_STATE);
			var hr = columnManager.GetColumnInfo(in allKeys[index], ref columnInfo);
			var width = hr.Succeeded && columnInfo.uWidth is > 0 and < 16384
				? columnInfo.uWidth * 96d / dpi
				: 120d;
			var isVisible = hr.Succeeded ? (columnInfo.dwState & (uint)CM_STATE.CM_STATE_VISIBLE) is not 0 : visiblePropertyIds.Contains(propertyId);
			columns.Add(new ViewColumnSettings(propertyId, Math.Max(1, width), index, isVisible));
		}

		var sortPropertyId = default(string);
		var sortDirection = ViewSortDirection.Ascending;
		Span<SORTCOLUMN> sortColumns = stackalloc SORTCOLUMN[1];
		if (folderView.GetSortColumns(sortColumns).Succeeded && !sortColumns.IsEmpty && WindowsShellColumnReader.GetPropertyId(sortColumns[0].propkey) is { } sortId)
		{
			sortPropertyId = sortId;
			sortDirection = sortColumns[0].direction is SORTDIRECTION.SORT_DESCENDING ? ViewSortDirection.Descending : ViewSortDirection.Ascending;
		}

		string? groupPropertyId = null;
		var groupDirection = ViewSortDirection.Ascending;
		var groupResult = folderView.GetGroupBy(out var groupKey, out var groupAscending);
		if (IsGrouped(groupResult))
		{
			groupPropertyId = WindowsShellColumnReader.GetPropertyId(groupKey);
			groupDirection = groupAscending.Value is 0 ? ViewSortDirection.Descending : ViewSortDirection.Ascending;
		}

		return new BrowseViewSettings(MapLayoutMode(viewMode), columns, sortPropertyId, sortDirection, groupPropertyId: groupPropertyId, groupDirection: groupDirection);
	}

	private static bool ApplyState(IFolderView2 folderView, IColumnManager columnManager, BrowseViewSettings current, BrowseViewSettingsOverride settingsOverride,
		HWND hostWindow, CancellationToken cancellationToken)
	{
		var settings = settingsOverride.ApplyTo(current);
		if (settingsOverride.Fields.HasFlag(ViewSettingsOverrideFields.LayoutMode) && settings.LayoutMode is not ViewLayoutMode.Columns)
		{
			var viewMode = MapViewMode(settings.LayoutMode);
			if (folderView.SetViewModeAndIconSize(viewMode, DefaultImageSize).Failed)
			{
				return false;
			}
		}

		if (settingsOverride.Fields.HasFlag(ViewSettingsOverrideFields.DetailsColumns))
		{
			if (!ApplyColumns(columnManager, settings.Columns, cancellationToken, hostWindow))
			{
				return false;
			}
		}

		if (settingsOverride.Fields.HasFlag(ViewSettingsOverrideFields.SortPropertyId) || settingsOverride.Fields.HasFlag(ViewSettingsOverrideFields.SortDirection))
		{
			if (settings.SortPropertyId is null || !WindowsShellColumnReader.TryGetPropertyKey(settings.SortPropertyId, out var sortKey))
			{
				return false;
			}

			var sortColumn = new SORTCOLUMN { propkey = sortKey, direction = settings.SortDirection is ViewSortDirection.Descending ? SORTDIRECTION.SORT_DESCENDING : SORTDIRECTION.SORT_ASCENDING };
			if (folderView.SetSortColumns([sortColumn]).Failed)
			{
				return false;
			}
		}

		if (settingsOverride.Fields.HasFlag(ViewSettingsOverrideFields.GroupPropertyId) || settingsOverride.Fields.HasFlag(ViewSettingsOverrideFields.GroupDirection))
		{
			if (settings.GroupPropertyId is null || !WindowsShellColumnReader.TryGetPropertyKey(settings.GroupPropertyId, out var groupKey))
			{
				return false;
			}

			if (folderView.SetGroupBy(in groupKey, settings.GroupDirection is ViewSortDirection.Ascending ? new BOOL(1) : new BOOL(0)).Failed)
			{
				return false;
			}
		}

		return true;
	}

	private static bool ApplyColumns(IColumnManager columnManager, IReadOnlyList<ViewColumnSettings> columns, CancellationToken cancellationToken, HWND hostWindow)
	{
		if (!TryGetColumnKeys(columnManager, CM_ENUM_FLAGS.CM_ENUM_ALL, cancellationToken, out var allKeys))
		{
			return false;
		}

		var settingsByPropertyId = columns.ToDictionary(static column => column.PropertyId, StringComparer.Ordinal);
		var visibleKeys = new List<PROPERTYKEY>(columns.Count);
		foreach (var column in columns.OrderBy(static column => column.Order))
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (column.IsVisible && WindowsShellColumnReader.TryGetPropertyKey(column.PropertyId, out var key) && allKeys.Any(candidate => candidate.Equals(key)))
			{
				visibleKeys.Add(key);
			}
		}

		if (columnManager.SetColumns(CollectionsMarshal.AsSpan(visibleKeys)).Failed)
		{
			return false;
		}

		var dpi = PInvoke.GetDpiForWindow(hostWindow);
		if (dpi is 0)
		{
			dpi = 96;
		}

		foreach (var key in allKeys)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var propertyId = WindowsShellColumnReader.GetPropertyId(key);
			if (!settingsByPropertyId.TryGetValue(propertyId, out var settings))
			{
				continue;
			}

			CM_COLUMNINFO columnInfo = default;
			columnInfo.cbSize = checked((uint)Marshal.SizeOf<CM_COLUMNINFO>());
			columnInfo.dwMask = (uint)(CM_MASK.CM_MASK_WIDTH | CM_MASK.CM_MASK_STATE);
			columnInfo.uWidth = checked((uint)Math.Clamp(Math.Round(settings.Width * dpi / 96d), 1, 16383));
			columnInfo.dwState = settings.IsVisible ? (uint)CM_STATE.CM_STATE_VISIBLE : 0;
			if (columnManager.SetColumnInfo(in key, in columnInfo).Failed)
			{
				return false;
			}
		}

		return true;
	}

	private static bool TryGetColumnKeys(IColumnManager columnManager, CM_ENUM_FLAGS flags, CancellationToken cancellationToken, out PROPERTYKEY[] keys)
	{
		keys = [];
		cancellationToken.ThrowIfCancellationRequested();

		var hr = columnManager.GetColumnCount(flags, out var count);
		if (hr.Failed || count is 0 or > MaximumColumnCount)
		{
			return false;
		}

		keys = new PROPERTYKEY[checked((int)count)];

		return columnManager.GetColumns(flags, keys).Succeeded;
	}

	private static ViewLayoutMode MapLayoutMode(FOLDERVIEWMODE viewMode)
	{
		return viewMode switch
		{
			FOLDERVIEWMODE.FVM_DETAILS => ViewLayoutMode.Details,
			FOLDERVIEWMODE.FVM_LIST or FOLDERVIEWMODE.FVM_SMALLICON => ViewLayoutMode.List,
			FOLDERVIEWMODE.FVM_TILE or FOLDERVIEWMODE.FVM_CONTENT => ViewLayoutMode.Cards,
			_ => ViewLayoutMode.Grid,
		};
	}

	private static FOLDERVIEWMODE MapViewMode(ViewLayoutMode layoutMode)
	{
		return layoutMode switch
		{
			ViewLayoutMode.Details => FOLDERVIEWMODE.FVM_DETAILS,
			ViewLayoutMode.List => FOLDERVIEWMODE.FVM_LIST,
			ViewLayoutMode.Cards => FOLDERVIEWMODE.FVM_TILE,
			_ => FOLDERVIEWMODE.FVM_ICON,
		};
	}

	[GeneratedComClass]
	private sealed partial class ExplorerBrowserEvents : IExplorerBrowserEvents
	{
		private readonly ManualResetEventSlim _navigationComplete = new(false);
		private int _navigationFailed;

		public HRESULT OnNavigationPending(ITEMIDLIST* pidlFolder)
		{
			return HRESULT.S_OK;
		}

		public HRESULT OnViewCreated(IShellView psv)
		{
			return HRESULT.S_OK;
		}

		public HRESULT OnNavigationComplete(ITEMIDLIST* pidlFolder)
		{
			_navigationComplete.Set();

			return HRESULT.S_OK;
		}

		public HRESULT OnNavigationFailed(ITEMIDLIST* pidlFolder)
		{
			Interlocked.Exchange(ref _navigationFailed, 1);
			_navigationComplete.Set();

			return HRESULT.S_OK;
		}

		internal bool WaitForNavigation(TimeSpan timeout, CancellationToken cancellationToken)
		{
			var completed = _navigationComplete.Wait(timeout, cancellationToken);

			return completed && Volatile.Read(ref _navigationFailed) is 0;
		}
	}
}
