// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Files.Infrastructure;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class RootView : UserControl, IDisposable
{
	private readonly RootViewModel _viewModel;

	private bool _isLoaded;

	private int _isDisposed;

	public RootViewModel ViewModel => _viewModel;

	public RootView(RootViewModel viewModel)
	{
		ArgumentNullException.ThrowIfNull(viewModel);

		InitializeComponent();
		_viewModel = viewModel;
		Loaded += RootView_Loaded;
	}

	public void AttachWindow(Window window) => TabStrip.AttachWindow(window);

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		Loaded -= RootView_Loaded;
		TabStrip.Dispose();
		_viewModel.Dispose();
	}

	private async void RootView_Loaded(object sender, RoutedEventArgs e)
	{
		if (_isLoaded)
		{
			return;
		}

		_isLoaded = true;
		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("RootView", "Loaded START");
		if (Sidebar.MenuItems.Count > 0)
		{
			Sidebar.SelectedItem = Sidebar.MenuItems[0];
		}

		try
		{
			await _viewModel.InitializeAsync();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			_viewModel.ReportOperationError(exception);
		}
		finally
		{
			UiDiagnosticLog.Write("RootView", $"Loaded END elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
		}
	}

	private async void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
	{
		if (!_isLoaded)
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
				await _viewModel.HomeCommand.ExecuteAsync();
			}
			else
			{
				await _viewModel.NavigateToNavigationItemAsync(item);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			_viewModel.ReportOperationError(exception);
		}
	}
}
