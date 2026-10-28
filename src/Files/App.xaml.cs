// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Views;
using Files.Commands;
using Files.Infrastructure;
using Files.Settings;
using Files.StorageOperations;
using Files.Core.Composition;
using Files.Core.Sessions;
using Files.Core.Storage.Windows;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using System.Threading;

namespace Files;

public partial class App : Application
{
	private FilesCoreRuntime? _runtime;
	private readonly List<MainWindow> _mainWindows = [];
	private readonly CommandRegistry _commandRegistry;
	private readonly WindowsStorageSource _windowsStorageSource;
	private readonly JsonViewSettingsStore _viewSettingsStore;
	private readonly AppSettingsService _settings;
	private readonly WinRTStorageOperationService _storageOperations;
	private readonly StorageOperationTracker _storageOperationTracker;
	private readonly Lock _windowsLock = new();
	private readonly Lock _shutdownLock = new();
	private Task? _shutdownTask;
	private int _hasStarted;

	internal AppSettingsService Settings => _settings;

	public App()
	{
		_windowsStorageSource = new WindowsStorageSource();
		_storageOperations = new WinRTStorageOperationService();
		_storageOperationTracker = new StorageOperationTracker(_storageOperations);
		_viewSettingsStore = new JsonViewSettingsStore(_windowsStorageSource, AppSettingsService.GetDefaultLocalFolderPath());
		_settings = new AppSettingsService(_windowsStorageSource);
		_settings.ApplyLanguage();
		InitializeComponent();
		_commandRegistry = AppCommandRegistration.Build();
		_settings.PropertyChanged += Settings_PropertyChanged;
	}

	protected override async void OnLaunched(LaunchActivatedEventArgs args)
	{
		await StartAsync().ConfigureAwait(true);
	}

	private async Task StartAsync()
	{
		if (Interlocked.Exchange(ref _hasStarted, 1) is not 0)
		{
			return;
		}

		try
		{
			await LaunchAsync().ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files failed to start: {exception}");
			await ShutdownAsync().ConfigureAwait(true);
			Exit();
		}
	}

	private async Task LaunchAsync()
	{
		var startTimestamp = Stopwatch.GetTimestamp();
		UIDiagnosticLog.Write("App", "Launch START");
		await _viewSettingsStore.InitializeAsync().ConfigureAwait(true);
		var currentRuntime = new FilesCoreBuilder(_viewSettingsStore, _storageOperations).UseStorageOperations(_storageOperations).AddWindowsStorage(_windowsStorageSource, enableOperations: false).Build();
		_runtime = currentRuntime;
		UIDiagnosticLog.Write("App", $"Runtime built elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

		await CreateWindowAsync(persistPlacement: true).ConfigureAwait(true);
		UIDiagnosticLog.Write("App", $"Main window activated elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
		try
		{
			await _storageOperations.ConnectAsync().ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files.BackgroundServer server failed to start: {exception}");
		}
	}

	private async Task CreateWindowAsync(bool persistPlacement)
	{
		if (_runtime is not { } runtime)
		{
			throw new InvalidOperationException("The Files runtime is not available.");
		}

		var coreWindow = await runtime.ShellSession.CreateWindowAsync().ConfigureAwait(true);
		UIDiagnosticLog.Write("App", "Core window created");
		if (coreWindow.ActiveTab?.ActivePane is null)
		{
			await runtime.ShellSession.CloseWindowAsync(coreWindow.Id).ConfigureAwait(true);

			throw new InvalidOperationException("Files.Core did not create an active pane.");
		}

		MainWindow? mainWindow = null;
		try
		{
			mainWindow = new MainWindow(
				coreWindow,
				runtime.Workspace,
				runtime.StorageOperations,
				_storageOperationTracker,
				_settings,
				runtime.WindowsShellPreviewSessions,
				_commandRegistry,
				persistPlacement,
				() => runtime.ShellSession.SetActiveWindow(coreWindow.Id),
				windowHandle => _storageOperations.SetActiveWindow(windowHandle),
				() => CloseWindowAsync(coreWindow.Id, mainWindow),
				() => CreateWindowAsync(persistPlacement: false));
			mainWindow.ApplyTheme(_settings.ThemeMode);
			lock (_windowsLock)
			{
				_mainWindows.Add(mainWindow);
			}

			mainWindow.Activate();
			try
			{
				await _storageOperations.RegisterWindowAsync(mainWindow.WindowHandle).ConfigureAwait(true);
			}
			catch (Exception exception)
			{
				Debug.WriteLine($"Files.BackgroundServer could not register a window: {exception}");
			}
		}
		catch
		{
			await runtime.ShellSession.CloseWindowAsync(coreWindow.Id).ConfigureAwait(true);

			throw;
		}
	}

	private async Task CloseWindowAsync(Guid windowId, MainWindow? mainWindow)
	{
		if (mainWindow is not null)
		{
			try
			{
				await _storageOperations.UnregisterWindowAsync(mainWindow.WindowHandle).ConfigureAwait(true);
			}
			catch (Exception exception)
			{
				Debug.WriteLine($"Files.BackgroundServer could not unregister a window: {exception}");
			}
		}

		if (_runtime is not { } runtime)
		{
			return;
		}

		bool closeRuntime;
		lock (_windowsLock)
		{
			if (mainWindow is not null)
			{
				_mainWindows.Remove(mainWindow);
			}

			closeRuntime = _mainWindows.Count is 0;
		}

		await runtime.ShellSession.CloseWindowAsync(windowId).ConfigureAwait(true);
		if (closeRuntime)
		{
			await ShutdownAsync().ConfigureAwait(true);
			Exit();
		}
	}

	private Task ShutdownAsync()
	{
		lock (_shutdownLock)
		{
			return _shutdownTask ??= ShutdownCoreAsync();
		}
	}

	private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is not nameof(AppSettingsService.ThemeMode))
		{
			return;
		}

		MainWindow[] windows;
		lock (_windowsLock)
		{
			windows = [.. _mainWindows];
		}

		foreach (var window in windows)
		{
			window.ApplyTheme(_settings.ThemeMode);
		}
	}

	private async Task ShutdownCoreAsync()
	{
		lock (_windowsLock)
		{
			_mainWindows.Clear();
		}

		try
		{
			_settings.Dispose();
			_viewSettingsStore.Dispose();

			if (Interlocked.Exchange(ref _runtime, null) is { } currentRuntime)
			{
				await currentRuntime.DisposeAsync().ConfigureAwait(true);
			}
		}
		finally
		{
			_settings.PropertyChanged -= Settings_PropertyChanged;
			_storageOperationTracker.Dispose();
			_storageOperations.Dispose();
		}
	}
}
