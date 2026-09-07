// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Views;
using Files.Activation;
using Files.Commands;
using Files.Core.Sessions;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.Capabilities.Previews;
using Files.Core.Storage;
using Files.Infrastructure;
using Files.Presentation;
using Files.ItemProperties;
using Files.Settings;
using Files.StorageOperations;
using Files.Core.Windows;
using Files.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using System.ComponentModel;
using WinRT.Interop;
using Windows.Win32.Foundation;

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
	private WindowsShellBrowserRegistration? _shellBrowserRegistration;
	private readonly bool _persistPlacement;
	private FolderBrowserViewModel? _shellBrowserFolder;
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
		_itemPropertiesService = new ItemPropertiesService(windowHandle, storageOperations, windowsSource is null ? null : new WindowsShellAppExtensionService(windowsSource));
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
		_rootView.ViewModel.PropertyChanged += RootViewModel_PropertyChanged;
		try
		{
			_shellBrowserRegistration = new WindowsShellBrowserRegistration((HWND)windowHandle);
			UpdateShellBrowserFolder();
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files failed to register the Shell browser view: {exception}");
		}

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
		_rootView.ViewModel.PropertyChanged -= RootViewModel_PropertyChanged;
		SetShellBrowserFolder(null);
		try
		{
			_shellBrowserRegistration?.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files failed to unregister the Shell browser view: {exception}");
		}

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

	private void RootViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
	{
		if (args.PropertyName is nameof(RootViewModel.ActiveTab) or nameof(RootViewModel.ActiveFolderBrowser) or null)
		{
			UpdateShellBrowserFolder();
		}
	}

	private void ShellBrowserFolder_PropertyChanged(object? sender, PropertyChangedEventArgs args)
	{
		if (args.PropertyName is nameof(FolderBrowserViewModel.Location) or null)
		{
			UpdateShellBrowserFolder();

			return;
		}

		if (args.PropertyName is nameof(FolderBrowserViewModel.SelectedKeys))
		{
			UpdateShellBrowserSelection();
		}
	}

	private void UpdateShellBrowserFolder()
	{
		var folder = _rootView.ViewModel.ActiveFolderBrowser;
		SetShellBrowserFolder(folder);
		var parsingName = GetShellParsingName(folder);
		try
		{
			if (_shellBrowserRegistration is { } registration)
			{
				registration.UpdateLocation(parsingName);
				registration.UpdateSelection(GetShellSelection(folder));
			}
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files failed to update the Shell browser view: {exception}");
		}
	}

	private void UpdateShellBrowserSelection()
	{
		try
		{
			_shellBrowserRegistration?.UpdateSelection(GetShellSelection(_rootView.ViewModel.ActiveFolderBrowser));
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files failed to update the Shell browser selection: {exception}");
		}
	}

	private static string? GetShellParsingName(FolderBrowserViewModel? folder)
	{
		if (folder?.Location is not FolderLocation { Folder.LastKnownAddress: { } address } ||
			(!address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase) &&
			!address.Scheme.Equals(WindowsStorageSource.ShellAddressScheme, StringComparison.OrdinalIgnoreCase)))
		{
			return null;
		}

		return address.Value;
	}

	private static IReadOnlyList<string> GetShellSelection(FolderBrowserViewModel? folder)
	{
		if (folder is null)
		{
			return [];
		}

		return folder.SelectedItems
			.Select(static item => item.Reference.LastKnownAddress)
			.Where(static address => address is not null &&
				(address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase) ||
				 address.Scheme.Equals(WindowsStorageSource.ShellAddressScheme, StringComparison.OrdinalIgnoreCase)))
			.Select(static address => address!.Value)
			.ToArray();
	}

	private void SetShellBrowserFolder(FolderBrowserViewModel? folder)
	{
		if (ReferenceEquals(_shellBrowserFolder, folder))
		{
			return;
		}

		if (_shellBrowserFolder is not null)
		{
			_shellBrowserFolder.PropertyChanged -= ShellBrowserFolder_PropertyChanged;
		}

		_shellBrowserFolder = folder;
		if (_shellBrowserFolder is not null)
		{
			_shellBrowserFolder.PropertyChanged += ShellBrowserFolder_PropertyChanged;
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
		SaveWindowPlacement();
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
