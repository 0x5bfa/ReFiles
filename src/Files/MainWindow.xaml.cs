// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Views;
using Files.Activation;
using Files.Commands;
using Files.Core.Sessions;
using Files.Core.Data;
using Files.Core.Capabilities.Previews;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Files.Infrastructure;
using Files.Presentation;
using Files.ItemProperties;
using Files.Settings;
using Files.StorageOperations;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using WinRT.Interop;

namespace Files;

public sealed partial class MainWindow : Window
{
	private static readonly Guid _mainWindowPersistedStateId = new("179e024a-24ec-4911-b93b-14e5b0a1856a");
	private readonly RootView _rootView;
	private readonly AppWindow _appWindow;
	private readonly Action _activateSession;
	private readonly Func<Task> _closeAsync;
	private readonly Func<Task> _createWindowAsync;
	private readonly ItemPropertiesService _itemPropertiesService;
	private readonly bool _persistPlacement;
	private int _closeStarted;
	private int _isDisposed;

	internal MainWindow(
		WindowSession coreWindow,
		IStorageWorkspace workspace,
		IStorageOperationService storageOperations,
		StorageOperationTracker operationTracker,
		AppSettingsService appSettings,
		IWindowsShellPreviewSessionFactory? windowsShellPreviewSessions,
		CommandRegistry commandRegistry,
		bool persistPlacement,
		Action activateSession,
		Func<Task> closeAsync,
		Func<Task> createWindowAsync)
	{
		ArgumentNullException.ThrowIfNull(coreWindow);

		ArgumentNullException.ThrowIfNull(workspace);

		ArgumentNullException.ThrowIfNull(storageOperations);

		ArgumentNullException.ThrowIfNull(operationTracker);

		ArgumentNullException.ThrowIfNull(appSettings);

		ArgumentNullException.ThrowIfNull(commandRegistry);

		ArgumentNullException.ThrowIfNull(activateSession);

		ArgumentNullException.ThrowIfNull(closeAsync);

		ArgumentNullException.ThrowIfNull(createWindowAsync);

		InitializeComponent();
		_activateSession = activateSession;
		_closeAsync = closeAsync;
		_createWindowAsync = createWindowAsync;
		var windowHandle = WindowNative.GetWindowHandle(this);
		var itemActivationService = new ItemActivationService(workspace, windowHandle);
		var windowsSource = workspace.Sources.OfType<WindowsStorageSource>().FirstOrDefault();
		_itemPropertiesService = new ItemPropertiesService(windowHandle, windowsSource is null ? null : new WindowsShellAppExtensionService(windowsSource));
		var presentationFactory = new WindowPresentationFactory(
			workspace,
			storageOperations,
			operationTracker,
			appSettings,
			new DispatcherQueueUIDispatcher(DispatcherQueue),
			commandRegistry,
			itemActivationService,
			_itemPropertiesService,
			windowHandle);
		_rootView = new RootView(presentationFactory.Create(coreWindow), windowsShellPreviewSessions);
		_rootView.ViewModel.CloseWindowAsync = CloseFromCommandAsync;
		RootContent.Content = _rootView;
		_rootView.AttachWindow(this);
		_rootView.NewWindowRequested += RootView_NewWindowRequested;

		_appWindow = AppWindow;
		_persistPlacement = persistPlacement;
		if (_persistPlacement)
		{
			_appWindow.PlacementRestorationBehavior = PlacementRestorationBehavior.AllowShowMaximized | PlacementRestorationBehavior.AllowShowArranged;
			_appWindow.PersistedStateId = _mainWindowPersistedStateId;
		}

		_appWindow.Closing += AppWindow_Closing;
		Activated += MainWindow_Activated;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_appWindow.Closing -= AppWindow_Closing;
		Activated -= MainWindow_Activated;
		_rootView.NewWindowRequested -= RootView_NewWindowRequested;
		_rootView.Dispose();
		_itemPropertiesService.Dispose();
	}

	internal void ApplyTheme(AppThemeMode themeMode)
	{
		RootContent.RequestedTheme = themeMode switch
		{
			AppThemeMode.Light => ElementTheme.Light,
			AppThemeMode.Dark => ElementTheme.Dark,
			_ => ElementTheme.Default,
		};
	}

	private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
	{
		if (args.WindowActivationState is not WindowActivationState.Deactivated)
		{
			_activateSession();
		}
	}

	private async void RootView_NewWindowRequested(object? sender, EventArgs e)
	{
		try
		{
			await _createWindowAsync().ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			_rootView.ReportOperationError(exception);
		}
	}

	private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
	{
		args.Cancel = true;
		if (Interlocked.Exchange(ref _closeStarted, 1) is not 0)
		{
			return;
		}

		try
		{
			await CompleteCloseAsync().ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files failed to close: {exception}");
		}
	}

	private Task CloseFromCommandAsync()
	{
		if (Interlocked.Exchange(ref _closeStarted, 1) is not 0)
		{
			return Task.CompletedTask;
		}

		return CompleteCloseAsync();
	}

	private async Task CompleteCloseAsync()
	{
		SaveWindowPlacement();
		try
		{
			await _rootView.DisposeAsync().ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files failed to dispose the root view: {exception}");
		}

		try
		{
			await _closeAsync().ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files failed to close the core window: {exception}");
		}
		finally
		{
			try
			{
				Dispose();
			}
			catch (Exception exception)
			{
				Debug.WriteLine($"Files failed to dispose the window: {exception}");
			}

			try
			{
				Close();
			}
			catch (Exception exception)
			{
				Debug.WriteLine($"Files failed to close the window: {exception}");
			}
		}
	}

	private void SaveWindowPlacement()
	{
		if (!_persistPlacement)
		{
			return;
		}

		try
		{
			_appWindow.SaveCurrentPlacement();
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files failed to save the window placement: {exception}");
		}
	}
}
