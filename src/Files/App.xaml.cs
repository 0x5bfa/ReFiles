// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using Files.Views;
using Files.Commands;
using Files.Infrastructure;
using Files.Core.Composition;
using Microsoft.UI.Xaml;

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
		var currentRuntime = new FilesCoreBuilder()
			.AddWindowsStorage()
			.Build();
		_runtime = currentRuntime;

		var coreWindow = await currentRuntime.ShellSession
			.CreateWindowAsync()
			.ConfigureAwait(true);
		if (coreWindow.ActiveTab?.ActivePane is null)
		{
			throw new InvalidOperationException("Files.Core did not create an active pane.");
		}

		_mainWindow = new MainWindow(coreWindow, currentRuntime.Workspace, _commandRegistry, () => currentRuntime.ShellSession.SetActiveWindow(coreWindow.Id), ShutdownAsync);
		_mainWindow.Activate();
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
