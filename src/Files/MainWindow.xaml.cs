// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Views;
using Files.Commands;
using Files.Core.Sessions;
using Files.Core.Data;
using Files.Core.Storage;
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
	private readonly Func<Task> _closeAsync;
	private readonly Func<Task> _createWindowAsync;
	private int _closeStarted;
	private int _isDisposed;

	public MainWindow(
		WindowSession coreWindow,
		IStorageWorkspace workspace,
		IStorageOperationService storageOperations,
		CommandRegistry commandRegistry,
		Action activateSession,
		Func<Task> closeAsync,
		Func<Task> createWindowAsync)
	{
		ArgumentNullException.ThrowIfNull(coreWindow);
		ArgumentNullException.ThrowIfNull(workspace);
		ArgumentNullException.ThrowIfNull(storageOperations);
		ArgumentNullException.ThrowIfNull(commandRegistry);
		ArgumentNullException.ThrowIfNull(activateSession);
		ArgumentNullException.ThrowIfNull(closeAsync);
		ArgumentNullException.ThrowIfNull(createWindowAsync);

		InitializeComponent();
		_activateSession = activateSession;
		_closeAsync = closeAsync;
		_createWindowAsync = createWindowAsync;
		var presentationFactory = new WindowPresentationFactory(workspace, storageOperations, new DispatcherQueueUIDispatcher(DispatcherQueue), commandRegistry);
		_rootView = new RootView(presentationFactory.Create(coreWindow));
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

		_rootView.Dispose();
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
