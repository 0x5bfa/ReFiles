// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Views;
using Files.Commands;
using Files.Core.Sessions;
using Files.Core.Data;
using Files.Infrastructure;
using Files.Presentation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace Files;

public sealed partial class MainWindow : Window
{
	private readonly RootView _rootView;
	private readonly AppWindow _appWindow;
	private readonly Action _activateSession;
	private readonly Func<Task> _shutdownAsync;
	private int _closeStarted;
	private int _isDisposed;

	public MainWindow(WindowSession coreWindow, IStorageWorkspace workspace, CommandRegistry commandRegistry, Action activateSession, Func<Task> shutdownAsync)
	{
		ArgumentNullException.ThrowIfNull(coreWindow);
		ArgumentNullException.ThrowIfNull(workspace);
		ArgumentNullException.ThrowIfNull(commandRegistry);
		ArgumentNullException.ThrowIfNull(activateSession);
		ArgumentNullException.ThrowIfNull(shutdownAsync);

		InitializeComponent();
		_activateSession = activateSession;
		_shutdownAsync = shutdownAsync;
		var presentationFactory = new WindowPresentationFactory(workspace, new DispatcherQueueUIDispatcher(DispatcherQueue), commandRegistry);
		_rootView = new RootView(presentationFactory.Create(coreWindow));
		RootContent.Content = _rootView;
		_rootView.AttachWindow(this);

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
		_rootView.Dispose();
	}

	private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
	{
		if (args.WindowActivationState is not WindowActivationState.Deactivated)
		{
			_activateSession();
		}
	}

	private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
	{
		args.Cancel = true;
		if (Interlocked.Exchange(ref _closeStarted, 1) is not 0)
		{
			return;
		}

		_rootView.Dispose();
		try
		{
			await _shutdownAsync().ConfigureAwait(true);
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
