// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Views;
using Files.Commands;
using Files.Core.AppModels;
using Files.Core.Data;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace Files;

public sealed partial class MainWindow : Window
{
	private readonly RootView rootView;
	private readonly AppWindow appWindow;
	private readonly Func<Task> shutdownAsync;
	private int closeStarted;
	private int isDisposed;

	public MainWindow(WindowModel coreWindow, IFilesDataRoot dataRoot, CommandRegistry commandRegistry, Func<Task> shutdownAsync)
	{
		ArgumentNullException.ThrowIfNull(coreWindow);
		ArgumentNullException.ThrowIfNull(dataRoot);
		ArgumentNullException.ThrowIfNull(commandRegistry);
		ArgumentNullException.ThrowIfNull(shutdownAsync);

		InitializeComponent();
		this.shutdownAsync = shutdownAsync;
		rootView = new RootView(coreWindow, dataRoot, DispatcherQueue, commandRegistry);
		RootFrame.Content = rootView;
		rootView.AttachWindow(this);

		appWindow = AppWindow;
		appWindow.Closing += AppWindow_Closing;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
		{
			return;
		}

		appWindow.Closing -= AppWindow_Closing;
		rootView.Dispose();
	}

	private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
	{
		args.Cancel = true;
		if (Interlocked.Exchange(ref closeStarted, 1) is not 0)
		{
			return;
		}

		rootView.Dispose();
		try
		{
			await shutdownAsync().ConfigureAwait(true);
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
