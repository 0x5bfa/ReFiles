// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Views;
using Files.Commands;
using Files.Infrastructure;
using Files.Core.Composition;
using Files.Core.Sessions;
using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace Files;

public partial class App : Application
{
	private FilesCoreRuntime? _runtime;
	private readonly List<MainWindow> _mainWindows = [];
	private readonly CommandRegistry _commandRegistry;
	private readonly Lock _windowsLock = new();
	private readonly Lock _shutdownLock = new();
	private Task? _shutdownTask;

	public App()
	{
		InitializeComponent();
		_commandRegistry = AppCommandRegistration.Build();
	}

	protected override async void OnLaunched(LaunchActivatedEventArgs args)
	{
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
		UiDiagnosticLog.Write("App", "Launch START");
		var currentRuntime = new FilesCoreBuilder()
			.AddWindowsStorage()
			.Build();
		_runtime = currentRuntime;
		UiDiagnosticLog.Write("App", $"Runtime built elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

		await CreateWindowAsync().ConfigureAwait(true);
		UiDiagnosticLog.Write("App", $"Main window activated elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
	}

	private async Task CreateWindowAsync()
	{
		if (_runtime is not { } runtime)
		{
			throw new InvalidOperationException("The Files runtime is not available.");
		}

		var coreWindow = await runtime.ShellSession.CreateWindowAsync().ConfigureAwait(true);
		UiDiagnosticLog.Write("App", "Core window created");
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
				_commandRegistry,
				() => runtime.ShellSession.SetActiveWindow(coreWindow.Id),
				() => CloseWindowAsync(coreWindow.Id, mainWindow),
				CreateWindowAsync);
			lock (_windowsLock)
			{
				_mainWindows.Add(mainWindow);
			}

			mainWindow.Activate();
		}
		catch
		{
			await runtime.ShellSession.CloseWindowAsync(coreWindow.Id).ConfigureAwait(true);

			throw;
		}
	}

	private async Task CloseWindowAsync(Guid windowId, MainWindow? mainWindow)
	{
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

	private async Task ShutdownCoreAsync()
	{
		lock (_windowsLock)
		{
			_mainWindows.Clear();
		}

		if (Interlocked.Exchange(ref _runtime, null) is { } currentRuntime)
		{
			await currentRuntime.DisposeAsync().ConfigureAwait(true);
		}
	}
}
