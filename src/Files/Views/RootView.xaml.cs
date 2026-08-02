// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Files.Commands;
using Files.Infrastructure;
using Files.Core.AppModels;
using Files.Core.Data;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class RootView : Page, IDisposable
{
	private readonly RootViewModel viewModel;

	private bool isLoaded;

	private int isDisposed;

	public RootViewModel ViewModel => viewModel;

	public RootView(WindowModel window, IFilesDataRoot dataRoot, DispatcherQueue dispatcherQueue, CommandRegistry commandRegistry)
	{
		InitializeComponent();
		viewModel = new RootViewModel(window, dataRoot, new DispatcherQueueUIDispatcher(dispatcherQueue), commandRegistry);
		Loaded += RootView_Loaded;
	}

	public void AttachWindow(Window window) => TabStrip.AttachWindow(window);

	public void Dispose()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
		{
			return;
		}

		Loaded -= RootView_Loaded;
		viewModel.Dispose();
	}

	private async void RootView_Loaded(object sender, RoutedEventArgs e)
	{
		if (isLoaded)
		{
			return;
		}

		isLoaded = true;
		if (Sidebar.MenuItems.Count > 0)
		{
			Sidebar.SelectedItem = Sidebar.MenuItems[0];
		}

		try
		{
			await viewModel.InitializeAsync();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			viewModel.ReportOperationError(exception);
		}
	}

	private async void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
	{
		if (!isLoaded)
		{
			return;
		}

		var item = args.InvokedItemContainer?.Tag as NavigationItemViewModel
			?? (args.InvokedItemContainer?.Content as NavigationViewItem)?.Tag as NavigationItemViewModel
			?? args.InvokedItem as NavigationItemViewModel;
		if (item is null)
		{
			return;
		}

		try
		{
			if (item.IsHome)
			{
				await viewModel.HomeCommand.ExecuteAsync();
			}
			else
			{
				await viewModel.NavigateToNavigationItemAsync(item);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			viewModel.ReportOperationError(exception);
		}
	}
}
