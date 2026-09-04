// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Adapters;
using Files.Controls;
using Files.Core.Browsing;
using Files.Core.Storage.Windows;
using Files.Infrastructure;
using Files.Localization;
using Files.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Files.Views;

public sealed partial class NavigationToolbar : UserControl
{
	private const double BreadcrumbIconSize = 16;
	private const string FolderIconGlyph = "\uE8B7";

	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(NavigationToolbarViewModel), typeof(NavigationToolbar), new PropertyMetadata(null));

	private CancellationTokenSource? _breadcrumbFlyoutCancellation;
	private CancellationTokenSource? _dragSourceCancellation;
	private WinUiShellDropTargetController? _dropController;
	private FolderBrowserViewModel? _dropBrowser;
	private long _dragSourceGeneration;

	public NavigationToolbarViewModel? ViewModel
	{
		get => (NavigationToolbarViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	internal event EventHandler? FolderViewFocusRequested;

	public NavigationToolbar()
	{
		InitializeComponent();
		NavigationButtons.AddHandler(PointerReleasedEvent, new PointerEventHandler(NavigationButtons_PointerReleased), true);
		Unloaded += NavigationToolbar_Unloaded;
	}

	private async void PathOmnibar_QuerySubmitted(Omnibar sender, OmnibarQuerySubmittedEventArgs args)
	{
		await NavigatePathAsync(args.Text);
	}

	private async void PathBreadcrumbBar_ItemClicked(Files.Controls.BreadcrumbBar sender, Files.Controls.BreadcrumbBarItemClickedEventArgs args)
	{
		if (ViewModel is not { } viewModel)
		{
			return;
		}

		if (args.IsRootItem)
		{
			await viewModel.NavigateHomeAsync();

			return;
		}

		if (args.Index < 0 || args.Index >= viewModel.BreadcrumbItems.Count)
		{
			return;
		}

		await viewModel.NavigateToBreadcrumbAsync(viewModel.BreadcrumbItems[args.Index]);
	}

	private async void PathBreadcrumbBar_ItemDropDownFlyoutOpening(object sender, BreadcrumbBarItemDropDownFlyoutEventArgs args)
	{
		if (ViewModel is not { } viewModel)
		{
			return;
		}

		BrowseLocation? location = args.IsRootItem
			? HomeLocation.Instance
			: args.Index >= 0 && args.Index < viewModel.BreadcrumbItems.Count
				? viewModel.BreadcrumbItems[args.Index].Location
				: null;
		if (location is null)
		{
			args.Flyout.Items.Clear();

			return;
		}

		_breadcrumbFlyoutCancellation?.Cancel();
		var cancellation = new CancellationTokenSource();
		_breadcrumbFlyoutCancellation = cancellation;
		args.Flyout.Items.Clear();
		args.Flyout.Items.Add(new MenuFlyoutItem { IsEnabled = false, Text = Strings.Loading.GetLocalized() });
		try
		{
			var children = await viewModel.GetBreadcrumbChildrenAsync(location, cancellation.Token);
			if (cancellation.IsCancellationRequested || !ReferenceEquals(_breadcrumbFlyoutCancellation, cancellation))
			{
				return;
			}

			args.Flyout.Items.Clear();
			foreach (var child in children)
			{
				var menuItem = new MenuFlyoutItem { Icon = await CreateBreadcrumbChildIconAsync(child), Tag = child, Text = child.Text };
				menuItem.Click += BreadcrumbChild_Click;
				args.Flyout.Items.Add(menuItem);
			}
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			UiDiagnosticLog.Write("NavigationToolbar", $"Breadcrumb child enumeration failed: {error.Message}");
			if (ReferenceEquals(_breadcrumbFlyoutCancellation, cancellation))
			{
				args.Flyout.Items.Clear();
			}
		}
		finally
		{
			if (ReferenceEquals(_breadcrumbFlyoutCancellation, cancellation))
			{
				_breadcrumbFlyoutCancellation = null;
			}

			cancellation.Dispose();
		}
	}

	private async void BreadcrumbItem_DragStarting(UIElement sender, DragStartingEventArgs args)
	{
		if (sender is not Files.Controls.BreadcrumbBarItem { Tag: NavigationToolbarBreadcrumbItem { ShellReference: { } reference } } || ViewModel?.ActiveFolderBrowser is not { } browser)
		{
			args.Cancel = true;

			return;
		}

		var generation = Interlocked.Increment(ref _dragSourceGeneration);
		_dragSourceCancellation?.Cancel();
		_dragSourceCancellation?.Dispose();
		var cancellation = new CancellationTokenSource();
		_dragSourceCancellation = cancellation;
		var deferral = args.GetDeferral();
		try
		{
			var dragSource = await browser.PrepareShellDragSourceAsync([reference], cancellation.Token);
			if (cancellation.IsCancellationRequested || generation != Volatile.Read(ref _dragSourceGeneration) || !ReferenceEquals(ViewModel?.ActiveFolderBrowser, browser) || dragSource is null)
			{
				args.Cancel = true;

				return;
			}

			var allowedOperations = WinUiDataObjectBridge.Attach(dragSource, args.Data, browser.OwnerWindowHandle, WindowsShellDropEffects.Link, deriveMoveFromDelete: false);
			args.AllowedOperations = allowedOperations;
			args.Cancel = allowedOperations is DataPackageOperation.None;
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			args.Cancel = true;
		}
		catch (Exception exception)
		{
			args.Cancel = true;
			browser.ReportOperationError(exception);
		}
		finally
		{
			if (ReferenceEquals(_dragSourceCancellation, cancellation))
			{
				_dragSourceCancellation = null;
			}

			cancellation.Dispose();
			deferral.Complete();
		}
	}

	private async void BreadcrumbItem_DragOver(object sender, DragEventArgs args)
	{
		if (sender is not Files.Controls.BreadcrumbBarItem { Tag: NavigationToolbarBreadcrumbItem { ShellReference: { } reference } } || ViewModel?.ActiveFolderBrowser is not { } browser)
		{
			args.AcceptedOperation = DataPackageOperation.None;
			args.Handled = true;
			_dropController?.DragLeave();

			return;
		}

		await GetDropController(browser).DragOverAsync(args, new(reference, false));
	}

	private async void BreadcrumbItem_DragEnter(object sender, DragEventArgs args)
	{
		if (sender is not Files.Controls.BreadcrumbBarItem { Tag: NavigationToolbarBreadcrumbItem { ShellReference: { } reference } } || ViewModel?.ActiveFolderBrowser is not { } browser)
		{
			args.AcceptedOperation = DataPackageOperation.None;
			args.Handled = true;
			_dropController?.DragLeave();

			return;
		}

		await GetDropController(browser).DragEnterAsync(args, new(reference, false));
	}

	private void BreadcrumbItem_DragLeave(object sender, DragEventArgs args)
	{
		_dropController?.DragLeave();
		args.Handled = true;
	}

	private async void BreadcrumbItem_Drop(object sender, DragEventArgs args)
	{
		if (sender is not Files.Controls.BreadcrumbBarItem { Tag: NavigationToolbarBreadcrumbItem { ShellReference: { } reference } } || ViewModel?.ActiveFolderBrowser is not { } browser)
		{
			args.AcceptedOperation = DataPackageOperation.None;
			args.Handled = true;
			_dropController?.DragLeave();

			return;
		}

		await GetDropController(browser).DropAsync(args, new(reference, false));
	}

	private void PathBreadcrumbBar_ItemDropDownFlyoutClosed(object sender, BreadcrumbBarItemDropDownFlyoutEventArgs args)
	{
		_breadcrumbFlyoutCancellation?.Cancel();
		args.Flyout.Items.Clear();
	}

	private void PathBreadcrumbBar_Tapped(object sender, TappedRoutedEventArgs e)
	{
		var source = e.OriginalSource as DependencyObject;
		while (source is not null && !ReferenceEquals(source, PathBreadcrumbBar))
		{
			if (source is Button)
			{
				return;
			}

			source = VisualTreeHelper.GetParent(source);
		}

		PathOmnibar.FocusTextBox();
	}

	private void NavigationButtons_PointerReleased(object sender, PointerRoutedEventArgs e)
	{
		var pointerUpdateKind = e.GetCurrentPoint(NavigationButtons).Properties.PointerUpdateKind;
		if ((e.Pointer.PointerDeviceType is PointerDeviceType.Mouse && pointerUpdateKind is not PointerUpdateKind.LeftButtonReleased) || !IsNavigationButtonSource(e.OriginalSource as DependencyObject))
		{
			return;
		}

		DispatcherQueue.TryEnqueue(() => FolderViewFocusRequested?.Invoke(this, EventArgs.Empty));
	}

	private async void BreadcrumbChild_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not MenuFlyoutItem { Tag: NavigationToolbarBreadcrumbItem item } || ViewModel is not { } viewModel)
		{
			return;
		}

		await viewModel.NavigateToBreadcrumbAsync(item);
	}

	private void NavigationToolbar_Unloaded(object sender, RoutedEventArgs e)
	{
		_breadcrumbFlyoutCancellation?.Cancel();
		_breadcrumbFlyoutCancellation = null;
		Interlocked.Increment(ref _dragSourceGeneration);
		_dragSourceCancellation?.Cancel();
		_dragSourceCancellation?.Dispose();
		_dragSourceCancellation = null;
		ResetDropController();
	}

	private WinUiShellDropTargetController GetDropController(FolderBrowserViewModel browser)
	{
		if (ReferenceEquals(_dropBrowser, browser) && _dropController is not null)
		{
			return _dropController;
		}

		ResetDropController();
		_dropBrowser = browser;
		_dropController = new(browser.PrepareShellDropTargetAsync, browser.OwnerWindowHandle);

		return _dropController;
	}

	private void ResetDropController()
	{
		_dropController?.Dispose();
		_dropController = null;
		_dropBrowser = null;
	}

	private bool IsNavigationButtonSource(DependencyObject? source)
	{
		for (var current = source; current is not null && !ReferenceEquals(current, NavigationButtons); current = VisualTreeHelper.GetParent(current))
		{
			if (current is ButtonBase)
			{
				return true;
			}
		}

		return false;
	}

	private static async Task<IconElement> CreateBreadcrumbChildIconAsync(NavigationToolbarBreadcrumbItem item)
	{
		if (item.Thumbnail is not { } thumbnail)
		{
			return new FontIcon { Glyph = FolderIconGlyph };
		}

		try
		{
			return new ImageIcon { Height = BreadcrumbIconSize, Source = await ThumbnailImageFactory.CreateAsync(thumbnail), Width = BreadcrumbIconSize };
		}
		catch (Exception error)
		{
			UiDiagnosticLog.Write("NavigationToolbar", $"Breadcrumb thumbnail decode failed: {error.GetType().Name}");

			return new FontIcon { Glyph = FolderIconGlyph };
		}
	}

	private async Task NavigatePathAsync(string path)
	{
		if (ViewModel is not { } viewModel || string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		await viewModel.NavigatePathCommand.ExecuteAsync(path);
	}
}
