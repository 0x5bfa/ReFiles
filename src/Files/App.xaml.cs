// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Views;
using Files.Commands;
using Files.Infrastructure;
using Files.Core.Composition;
using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace Files;

public partial class App : Application
{
	private FilesCoreRuntime? _runtime;
	private MainWindow? _mainWindow;
	private readonly CommandRegistry _commandRegistry;
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

		var coreWindow = await currentRuntime.ShellSession
			.CreateWindowAsync()
			.ConfigureAwait(true);
		UiDiagnosticLog.Write("App", $"Core window created elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
		if (coreWindow.ActiveTab?.ActivePane is null)
		{
			throw new InvalidOperationException("Files.Core did not create an active pane.");
		}

		_mainWindow = new MainWindow(coreWindow, currentRuntime.Workspace, _commandRegistry, () => currentRuntime.ShellSession.SetActiveWindow(coreWindow.Id), ShutdownAsync);
		_mainWindow.Activate();
		UiDiagnosticLog.Write("App", $"Main window activated elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
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
		_mainWindow = null;

		if (Interlocked.Exchange(ref _runtime, null) is { } currentRuntime)
		{
			await currentRuntime.DisposeAsync().ConfigureAwait(true);
		}
	}
}
