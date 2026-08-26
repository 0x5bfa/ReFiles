// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Views;
using Files.Activation;
using Files.Commands;
using Files.Core.Sessions;
using Files.Core.Data;
using Files.Core.ItemFeatures.Previews;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Files.Infrastructure;
using Files.Presentation;
using Files.ItemProperties;
using Files.Settings;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using WinRT.Interop;

namespace Files;

public sealed partial class MainWindow : Window
{
	private readonly RootView _rootView;
	private readonly AppWindow _appWindow;
	private readonly Action _activateSession;
	private readonly Func<Task> _closeAsync;
	private readonly Func<Task> _createWindowAsync;
	private readonly ItemPropertiesService _itemPropertiesService;
	private int _closeStarted;
	private int _isDisposed;

	internal MainWindow(
		WindowSession coreWindow,
		IStorageWorkspace workspace,
		IStorageOperationService storageOperations,
		AppSettingsService appSettings,
		IWindowsShellPreviewSessionFactory? windowsShellPreviewSessions,
		CommandRegistry commandRegistry,
		Action activateSession,
		Func<Task> closeAsync,
		Func<Task> createWindowAsync)
	{
		ArgumentNullException.ThrowIfNull(coreWindow);

		ArgumentNullException.ThrowIfNull(workspace);

		ArgumentNullException.ThrowIfNull(storageOperations);

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

		await CompleteCloseAsync().ConfigureAwait(true);
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
		await _rootView.DisposeAsync().ConfigureAwait(true);
		try
		{
			await _closeAsync().ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files failed to shut down cleanly: {exception}");
		}
		finally
		{
			Dispose();
			Close();
		}
	}
}
