// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Files.Controls;
using Files.Infrastructure;
using Files.Core.Capabilities.Previews;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Files.Views;

public sealed partial class RootView : UserControl, IDisposable, IAsyncDisposable
{
	private const double PreviewPaneWidth = 320;

	private readonly RootViewModel _viewModel;
	private readonly Queue<string> _pendingErrorMessages = [];

	private CancellationTokenSource? _sidebarDragSourceCancellation;
	private WinUiShellDropTargetController? _sidebarDropController;
	private FolderBrowserViewModel? _sidebarDropBrowser;
	private Task _showErrorDialogsTask = Task.CompletedTask;
	private OperationErrorDialog? _activeErrorDialog;
	private bool _isLoaded;
	private long _sidebarDragSourceGeneration;

	private int _isDisposed;

	public RootViewModel ViewModel => _viewModel;

	public event EventHandler? NewWindowRequested;

	public RootView(RootViewModel viewModel, IWindowsShellPreviewSessionFactory? previewSessionFactory)
	{
		ArgumentNullException.ThrowIfNull(viewModel);

		InitializeComponent();
		_viewModel = viewModel;
		_viewModel.PropertyChanged += ViewModel_PropertyChanged;
		_viewModel.OperationErrorReported += ViewModel_OperationErrorReported;
		PreviewPaneView.SessionFactory = previewSessionFactory;
		TabStrip.NewWindowRequested += TabStrip_NewWindowRequested;
		NavigationToolbarView.FolderViewFocusRequested += NavigationToolbarView_FolderViewFocusRequested;
		Loaded += RootView_Loaded;
	}

	public void AttachWindow(Window window)
	{
		ArgumentNullException.ThrowIfNull(window);

		TabStrip.AttachWindow(window);
		PreviewPaneView.AttachWindow(window);
	}

	public void ReportOperationError(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);

		_viewModel.ReportOperationError(exception);
	}

	public void Dispose()
	{
		_ = DisposeAsync();
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		Loaded -= RootView_Loaded;
		CancelSidebarDragSource();
		ResetSidebarDropController();
		_viewModel.PropertyChanged -= ViewModel_PropertyChanged;
		_viewModel.OperationErrorReported -= ViewModel_OperationErrorReported;
		TabStrip.NewWindowRequested -= TabStrip_NewWindowRequested;
		NavigationToolbarView.FolderViewFocusRequested -= NavigationToolbarView_FolderViewFocusRequested;
		_pendingErrorMessages.Clear();
		_activeErrorDialog?.Hide();
		await _showErrorDialogsTask;
		await PreviewPaneView.DisposeAsync();
		TabStrip.Dispose();
		await _viewModel.DisposeAsync();
	}

	private void TabStrip_NewWindowRequested(object? sender, EventArgs e) =>
		NewWindowRequested?.Invoke(this, e);

	private void NavigationToolbarView_FolderViewFocusRequested(object? sender, EventArgs e) =>
		PaneHostView.FocusActiveFolderView();

	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is null or nameof(RootViewModel.ActiveTab) or nameof(RootViewModel.ActiveFolderBrowser))
		{
			CancelSidebarDragSource();
			ResetSidebarDropController();
		}

		if (e.PropertyName is null or nameof(RootViewModel.ActiveTab))
		{
			UpdateActiveTabPresentation();
		}
	}

	private void ViewModel_OperationErrorReported(object? sender, OperationErrorEventArgs e)
	{
		if ((_activeErrorDialog is not null && string.Equals(_activeErrorDialog.Message, e.Message, StringComparison.Ordinal)) || _pendingErrorMessages.Contains(e.Message))
		{
			return;
		}

		_pendingErrorMessages.Enqueue(e.Message);
		StartShowingErrorDialogs();
	}

	private void StartShowingErrorDialogs()
	{
		if (!_isLoaded || XamlRoot is null || !_showErrorDialogsTask.IsCompleted || Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		_showErrorDialogsTask = ShowErrorDialogsAsync();
	}

	private async Task ShowErrorDialogsAsync()
	{
		while (Volatile.Read(ref _isDisposed) is 0 && _pendingErrorMessages.TryDequeue(out var message))
		{
			_activeErrorDialog = new OperationErrorDialog(message) { XamlRoot = XamlRoot };
			try
			{
				await _activeErrorDialog.ShowAsync();
			}
			catch (Exception exception)
			{
				UiDiagnosticLog.Write("RootView", $"Error dialog failed type={exception.GetType().Name}");

				return;
			}
			finally
			{
				_activeErrorDialog = null;
			}
		}
	}

	private void UpdateActiveTabPresentation()
	{
		var isSettings = ViewModel.ActiveTab?.IsSettings is true;
		NavigationToolbarView.Visibility = isSettings ? Visibility.Collapsed : Visibility.Visible;
		FolderToolbarView.Visibility = isSettings ? Visibility.Collapsed : Visibility.Visible;
		PreviewPaneColumn.Width = new GridLength(isSettings ? 0 : PreviewPaneWidth);
		PreviewPaneView.Visibility = isSettings ? Visibility.Collapsed : Visibility.Visible;
		if (isSettings)
		{
			Sidebar.SelectedItem = ViewModel.SettingsNavigationItem;
		}
		else if (ReferenceEquals(Sidebar.SelectedItem, ViewModel.SettingsNavigationItem))
		{
			Sidebar.SelectedItem = null;
		}
	}

	private async void RootView_Loaded(object sender, RoutedEventArgs e)
	{
		if (_isLoaded)
		{
			return;
		}

		_isLoaded = true;
		StartShowingErrorDialogs();
		UpdateActiveTabPresentation();
		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("RootView", "Loaded START");
		Sidebar.SelectedItem ??= ViewModel.HomeNavigationItem;

		try
		{
			await _viewModel.InitializeAsync();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			_viewModel.ReportOperationError(exception);
		}
		finally
		{
			UiDiagnosticLog.Write("RootView", $"Loaded END elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
		}
	}

	private async void Sidebar_ItemInvoked(object? sender, ItemInvokedEventArgs args)
	{
		if (!_isLoaded)
		{
			return;
		}

		if (sender is not SidebarItem { Item: NavigationItemViewModel item })
		{
			return;
		}

		try
		{
			await _viewModel.NavigateToNavigationItemAsync(item);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			_viewModel.ReportOperationError(exception);
		}
	}

	private async void Sidebar_ItemDragStarting(object? sender, ItemDragStartingEventArgs args)
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || args.DragItem is not NavigationItemViewModel { Reference: { } reference } || ViewModel.ActiveFolderBrowser is not { } browser)
		{
			args.RawEvent.Cancel = true;

			return;
		}

		var generation = Interlocked.Increment(ref _sidebarDragSourceGeneration);
		_sidebarDragSourceCancellation?.Cancel();
		_sidebarDragSourceCancellation?.Dispose();
		var cancellation = new CancellationTokenSource();
		_sidebarDragSourceCancellation = cancellation;
		var deferral = args.RawEvent.GetDeferral();
		try
		{
			var dragSource = await browser.PrepareShellDragSourceAsync([reference], cancellation.Token);
			if (Volatile.Read(ref _isDisposed) is not 0 || cancellation.IsCancellationRequested || generation != Volatile.Read(ref _sidebarDragSourceGeneration)
				|| !ReferenceEquals(ViewModel.ActiveFolderBrowser, browser) || dragSource is null)
			{
				args.RawEvent.Cancel = true;

				return;
			}

			var allowedOperations = WinUiDataObjectBridge.Attach(dragSource, args.RawEvent.Data, browser.OwnerWindowHandle);
			args.RawEvent.AllowedOperations = allowedOperations;
			args.RawEvent.Cancel = allowedOperations is DataPackageOperation.None;
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			args.RawEvent.Cancel = true;
		}
		catch (Exception exception)
		{
			args.RawEvent.Cancel = true;
			_viewModel.ReportOperationError(exception);
		}
		finally
		{
			if (ReferenceEquals(_sidebarDragSourceCancellation, cancellation))
			{
				_sidebarDragSourceCancellation = null;
			}

			cancellation.Dispose();
			deferral.Complete();
		}
	}

	private async void Sidebar_ItemDragOver(object? sender, ItemDragOverEventArgs args)
	{
		if (args.DropTarget is not NavigationItemViewModel { Reference: { } reference } || ViewModel.ActiveFolderBrowser is not { } browser)
		{
			args.RawEvent.AcceptedOperation = DataPackageOperation.None;
			args.RawEvent.Handled = true;
			_sidebarDropController?.DragLeave();

			return;
		}

		await GetSidebarDropController(browser).DragOverAsync(args.RawEvent, new(reference, false));
	}

	private async void Sidebar_ItemDragEnter(object? sender, ItemDragEnterEventArgs args)
	{
		if (args.DropTarget is not NavigationItemViewModel { Reference: { } reference } || ViewModel.ActiveFolderBrowser is not { } browser)
		{
			args.RawEvent.AcceptedOperation = DataPackageOperation.None;
			args.RawEvent.Handled = true;
			_sidebarDropController?.DragLeave();

			return;
		}

		await GetSidebarDropController(browser).DragEnterAsync(args.RawEvent, new(reference, false));
	}

	private void Sidebar_ItemDragLeave(object? sender, ItemDragLeaveEventArgs args)
	{
		_sidebarDropController?.DragLeave();
		args.RawEvent.Handled = true;
	}

	private async void Sidebar_ItemDropped(object? sender, ItemDroppedEventArgs args)
	{
		if (args.DropTarget is not NavigationItemViewModel { Reference: { } reference } || ViewModel.ActiveFolderBrowser is not { } browser)
		{
			args.RawEvent.AcceptedOperation = DataPackageOperation.None;
			args.RawEvent.Handled = true;
			_sidebarDropController?.DragLeave();

			return;
		}

		await GetSidebarDropController(browser).DropAsync(args.RawEvent, new(reference, false));
	}

	private WinUiShellDropTargetController GetSidebarDropController(FolderBrowserViewModel browser)
	{
		if (ReferenceEquals(_sidebarDropBrowser, browser) && _sidebarDropController is not null)
		{
			return _sidebarDropController;
		}

		ResetSidebarDropController();
		_sidebarDropBrowser = browser;
		_sidebarDropController = new(browser.PrepareShellDropTargetAsync, browser.OwnerWindowHandle);

		return _sidebarDropController;
	}

	private void CancelSidebarDragSource()
	{
		Interlocked.Increment(ref _sidebarDragSourceGeneration);
		_sidebarDragSourceCancellation?.Cancel();
		_sidebarDragSourceCancellation?.Dispose();
		_sidebarDragSourceCancellation = null;
	}

	private void ResetSidebarDropController()
	{
		_sidebarDropController?.Dispose();
		_sidebarDropController = null;
		_sidebarDropBrowser = null;
	}
}
