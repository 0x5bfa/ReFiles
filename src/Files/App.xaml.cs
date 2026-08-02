// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Views;
using Files.Commands;
using Files.Core.Composition;
using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace Files;

public partial class App : Application
{
	private FilesCoreRuntime? runtime;
	private MainWindow? mainWindow;
	private readonly CommandRegistry commandRegistry;
	private readonly Lock shutdownLock = new();
	private Task? shutdownTask;

	public App()
	{
		InitializeComponent();
		commandRegistry = AppCommandRegistration.Build();
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
		runtime = new FilesCoreBuilder()
			.AddWindowsStorage()
			.Build();

		var coreWindow = await runtime.Application
			.CreateWindowAsync()
			.ConfigureAwait(true);
		if (coreWindow.ActiveTab?.ActivePane is null)
		{
			throw new InvalidOperationException("Files.Core did not create an active pane.");
		}

		mainWindow = new MainWindow(coreWindow, runtime.DataRoot, commandRegistry, ShutdownAsync);
		mainWindow.Activate();
	}

	private Task ShutdownAsync()
	{
		lock (shutdownLock)
		{
			return shutdownTask ??= ShutdownCoreAsync();
		}
	}

	private async Task ShutdownCoreAsync()
	{
		mainWindow = null;

		if (Interlocked.Exchange(ref runtime, null) is { } currentRuntime)
		{
			await currentRuntime.DisposeAsync().ConfigureAwait(true);
		}
	}
}
