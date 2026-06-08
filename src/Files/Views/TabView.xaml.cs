// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using NativeTabView = Microsoft.UI.Xaml.Controls.TabView;

namespace Files.Views;

public sealed partial class TabView : UserControl
{
	private AppWindow? appWindow;

	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(
			nameof(ViewModel),
			typeof(TabStripViewModel),
			typeof(TabView),
			new PropertyMetadata(null));

	public TabView()
	{
		InitializeComponent();
	}

	public TabStripViewModel? ViewModel
	{
		get => (TabStripViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public void AttachWindow(Window window)
	{
		ArgumentNullException.ThrowIfNull(window);

		var windowHandle = WindowNative.GetWindowHandle(window);
		var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
		appWindow = AppWindow.GetFromWindowId(windowId);
		appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
		appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
		appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
		appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
		appWindow.TitleBar.ButtonHoverBackgroundColor = Colors.Transparent;
		appWindow.TitleBar.ButtonPressedBackgroundColor = Colors.Transparent;

		window.SetTitleBar(TitleBarDragRegion);
	}

	private void NativeTabView_SelectionChanged(
		object sender,
		SelectionChangedEventArgs args)
	{
		if (ViewModel is { } viewModel
			&& sender is NativeTabView tabView)
		{
			viewModel.SetActiveTabAt(tabView.SelectedIndex);
		}
	}

	private async void NativeTabView_TabCloseRequested(
		NativeTabView sender,
		TabViewTabCloseRequestedEventArgs args)
	{
		if (ViewModel is not { } viewModel
			|| args.Item is not TabViewModel tab)
		{
			return;
		}

		await viewModel.CloseTabCommand.ExecuteAsync(tab);
	}
}
