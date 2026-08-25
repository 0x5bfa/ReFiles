// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.WinUI;
using Files.Localization;
using Files.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using WinRT.Interop;
using NativeTabView = Microsoft.UI.Xaml.Controls.TabView;

namespace Files.Views;

public sealed partial class TabView : UserControl, IDisposable
{
	private Window? _window;
	private AppWindow? _appWindow;
	private ContentPresenter? _leftContentPresenter;
	private TabViewListView? _tabViewListView;
	private ContentPresenter? _rightContentPresenter;
	private XamlRoot? _xamlRoot;
	private int _isDisposed;

	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(TabStripViewModel), typeof(TabView), new PropertyMetadata(null));

	public TabStripViewModel? ViewModel
	{
		get => (TabStripViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public event EventHandler? NewWindowRequested;

	public TabView()
	{
		InitializeComponent();
		Loaded += TabView_Loaded;
		SizeChanged += TitleBarElement_SizeChanged;
	}

	public void AttachWindow(Window window)
	{
		ArgumentNullException.ThrowIfNull(window);

		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

		if (_appWindow is not null)
		{
			_appWindow.Changed -= AppWindow_Changed;
		}

		var windowHandle = WindowNative.GetWindowHandle(window);
		var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
		_window = window;
		_appWindow = AppWindow.GetFromWindowId(windowId);
		_appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
		_appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
		_appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
		_appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
		_appWindow.TitleBar.ButtonHoverBackgroundColor = Colors.Transparent;
		_appWindow.TitleBar.ButtonPressedBackgroundColor = Colors.Transparent;
		_appWindow.Changed += AppWindow_Changed;
		UpdateTitleBarDragRegion();
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		Loaded -= TabView_Loaded;
		SizeChanged -= TitleBarElement_SizeChanged;
		if (_appWindow is not null)
		{
			_appWindow.Changed -= AppWindow_Changed;
		}

		if (_xamlRoot is not null)
		{
			_xamlRoot.Changed -= XamlRoot_Changed;
		}

		if (_tabViewListView is not null)
		{
			_tabViewListView.SizeChanged -= TitleBarElement_SizeChanged;
		}

		if (_rightContentPresenter is not null)
		{
			_rightContentPresenter.SizeChanged -= TitleBarElement_SizeChanged;
		}

		if (_leftContentPresenter is not null)
		{
			_leftContentPresenter.SizeChanged -= TitleBarElement_SizeChanged;
		}
	}

	private void NativeTabView_SelectionChanged(object sender, SelectionChangedEventArgs args)
	{
		if (ViewModel is { } viewModel && sender is NativeTabView tabView)
		{
			viewModel.SetActiveTabAt(tabView.SelectedIndex);
		}
	}

	private void NewWindowMenuItem_Click(object sender, RoutedEventArgs e) =>
		NewWindowRequested?.Invoke(this, EventArgs.Empty);

	private void CompactOverlayMenuItem_Click(object sender, RoutedEventArgs e)
	{
		if (_appWindow is null)
		{
			return;
		}

		_appWindow.SetPresenter(_appWindow.Presenter.Kind is AppWindowPresenterKind.CompactOverlay
			? AppWindowPresenterKind.Overlapped
			: AppWindowPresenterKind.CompactOverlay);
		UpdateCompactOverlayMenu();
	}

	private async void NativeTabView_TabCloseRequested(NativeTabView sender, TabViewTabCloseRequestedEventArgs args)
	{
		if (ViewModel is not { } viewModel || args.Item is not TabViewModel tab)
		{
			return;
		}

		await viewModel.CloseTabCommand.ExecuteAsync(tab);
	}

	private void TabView_Loaded(object sender, RoutedEventArgs args)
	{
		EnsureTitleBarElements();
		UpdateCompactOverlayMenu();
		UpdateTitleBarDragRegion();
	}

	private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
	{
		UpdateCompactOverlayMenu();

		if (DispatcherQueue.HasThreadAccess)
		{
			UpdateTitleBarDragRegion();

			return;
		}

		DispatcherQueue.TryEnqueue(UpdateTitleBarDragRegion);
	}

	private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateTitleBarDragRegion();

	private void TitleBarElement_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateTitleBarDragRegion();

	private void UpdateCompactOverlayMenu()
	{
		if (_appWindow is null || ViewModel is null)
		{
			return;
		}

		CompactOverlayMenuItem.Text = _appWindow.Presenter.Kind is AppWindowPresenterKind.CompactOverlay
			? LocalizationExtensions.GetLocalized(Strings.ExitCompactOverlay)
			: LocalizationExtensions.GetLocalized(Strings.EnterCompactOverlay);
	}

	private void EnsureTitleBarElements()
	{
		if (_leftContentPresenter is null || _tabViewListView is null || _rightContentPresenter is null)
		{
			NativeTabView.ApplyTemplate();
		}

		if (_leftContentPresenter is null && NativeTabView.FindDescendant<ContentPresenter>(static presenter => presenter.Name is "LeftContentPresenter") is { } leftContentPresenter)
		{
			_leftContentPresenter = leftContentPresenter;
			_leftContentPresenter.SizeChanged += TitleBarElement_SizeChanged;
		}

		if (_tabViewListView is null && NativeTabView.FindDescendant<TabViewListView>() is { } tabViewListView)
		{
			_tabViewListView = tabViewListView;
			_tabViewListView.SizeChanged += TitleBarElement_SizeChanged;
		}

		if (_rightContentPresenter is null && NativeTabView.FindDescendant<ContentPresenter>(static presenter => presenter.Name is "RightContentPresenter") is { } rightContentPresenter)
		{
			_rightContentPresenter = rightContentPresenter;
			_rightContentPresenter.SizeChanged += TitleBarElement_SizeChanged;
		}

		if (!ReferenceEquals(_xamlRoot, XamlRoot))
		{
			if (_xamlRoot is not null)
			{
				_xamlRoot.Changed -= XamlRoot_Changed;
			}

			_xamlRoot = XamlRoot;
			if (_xamlRoot is not null)
			{
				_xamlRoot.Changed += XamlRoot_Changed;
			}
		}
	}

	private void UpdateTitleBarDragRegion()
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || _window is null || _appWindow is null || !_appWindow.IsVisible)
		{
			return;
		}

		if (_window.Content is not UIElement rootElement || rootElement.XamlRoot is not { } xamlRoot)
		{
			return;
		}

		EnsureTitleBarElements();
		var source = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
		var scaleFactor = xamlRoot.RasterizationScale;
		var windowSize = _appWindow.Size;
		source.ClearRegionRects(NonClientRegionKind.Passthrough);
		var titleBarHeight = SetTitleBarDragRegion(source, scaleFactor);
		if (titleBarHeight < 0)
		{
			return;
		}

		const int borderThickness = 5;
		var logicalWidth = (int)Math.Ceiling(windowSize.Width / scaleFactor);
		source.SetRegionRects(NonClientRegionKind.LeftBorder, [GetScaledRect(rootElement, new RectInt32(0, 0, borderThickness, titleBarHeight))]);
		source.SetRegionRects(NonClientRegionKind.RightBorder, [GetScaledRect(rootElement, new RectInt32(Math.Max(0, logicalWidth - borderThickness), 0, borderThickness, titleBarHeight))]);
		source.SetRegionRects(NonClientRegionKind.Caption, [GetScaledRect(rootElement, new RectInt32(0, 0, logicalWidth, titleBarHeight))]);
	}

	private int SetTitleBarDragRegion(InputNonClientPointerSource source, double scaleFactor)
	{
		if (_leftContentPresenter is null || _tabViewListView is null || _rightContentPresenter is null || ActualHeight <= 0)
		{
			return -1;
		}

		var leftContentRect = GetScaledRect(_leftContentPresenter);
		var tabListRect = GetScaledRect(_tabViewListView);
		var rightContentRect = GetScaledRect(_rightContentPresenter);
		var padding = _tabViewListView.Padding;
		var tabListLeft = tabListRect.X + ScaleLength(padding.Left, scaleFactor);
		var tabListTop = tabListRect.Y + ScaleLength(padding.Top, scaleFactor);
		var tabListRight = tabListRect.X + tabListRect.Width - ScaleLength(padding.Right, scaleFactor);
		var rightContentRight = rightContentRect.X + Math.Min(rightContentRect.Width, ScaleLength(TabBarAddNewTabButton.ActualWidth, scaleFactor));
		var passthroughLeft = Math.Min(leftContentRect.X, tabListLeft);
		var passthroughTop = Math.Min(leftContentRect.Y, tabListTop);
		var passthroughRight = Math.Max(tabListRight, rightContentRight);
		var passthroughBottom = Math.Max(leftContentRect.Y + leftContentRect.Height, tabListRect.Y + tabListRect.Height - ScaleLength(padding.Bottom, scaleFactor));
		var passthroughWidth = Math.Max(0, passthroughRight - passthroughLeft);
		var passthroughHeight = Math.Max(0, passthroughBottom - passthroughTop);
		if (passthroughWidth > 0 && passthroughHeight > 0)
		{
			source.SetRegionRects(NonClientRegionKind.Passthrough, [new RectInt32(passthroughLeft, passthroughTop, passthroughWidth, passthroughHeight)]);
		}

		return (int)Math.Ceiling(ActualHeight);
	}

	private static int ScaleLength(double value, double scaleFactor) => (int)Math.Round(value * scaleFactor);

	private static RectInt32 GetScaledRect(UIElement element, RectInt32? logicalRect = null)
	{
		var scaleFactor = element.XamlRoot?.RasterizationScale ?? 1d;
		if (logicalRect is { } rect)
		{
			return new RectInt32(
				(int)Math.Round(rect.X * scaleFactor),
				(int)Math.Round(rect.Y * scaleFactor),
				(int)Math.Round(rect.Width * scaleFactor),
				(int)Math.Round(rect.Height * scaleFactor));
		}

		var offset = element.TransformToVisual(null).TransformPoint(default);

		return new RectInt32(
			(int)Math.Round(offset.X * scaleFactor),
			(int)Math.Round(offset.Y * scaleFactor),
			(int)Math.Round(element.ActualSize.X * scaleFactor),
			(int)Math.Round(element.ActualSize.Y * scaleFactor));
	}
}
