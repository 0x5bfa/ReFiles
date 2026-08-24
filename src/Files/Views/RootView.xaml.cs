// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Files.Controls;
using Files.Infrastructure;
using Files.Core.ItemFeatures.Previews;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class RootView : UserControl, IDisposable, IAsyncDisposable
{
	private readonly RootViewModel _viewModel;

	private bool _isLoaded;

	private int _isDisposed;

	public RootViewModel ViewModel => _viewModel;

	public event EventHandler? NewWindowRequested;

	public RootView(RootViewModel viewModel, IWindowsShellPreviewSessionFactory? previewSessionFactory)
	{
		ArgumentNullException.ThrowIfNull(viewModel);

		InitializeComponent();
		_viewModel = viewModel;
		// PreviewPaneView.SessionFactory = previewSessionFactory;
		TabStrip.NewWindowRequested += TabStrip_NewWindowRequested;
		Loaded += RootView_Loaded;
	}

	public void AttachWindow(Window window)
	{
		ArgumentNullException.ThrowIfNull(window);

		TabStrip.AttachWindow(window);
		// PreviewPaneView.AttachWindow(window);
	}

	public void ReportOperationError(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);

		_viewModel.ReportOperationError(exception);
	}

	public void Dispose()
	{
		_ = DisposeAsync();
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		Loaded -= RootView_Loaded;
		TabStrip.NewWindowRequested -= TabStrip_NewWindowRequested;
		// await PreviewPaneView.DisposeAsync();
		TabStrip.Dispose();
		await _viewModel.DisposeAsync();
	}

	private void TabStrip_NewWindowRequested(object? sender, EventArgs e) =>
		NewWindowRequested?.Invoke(this, e);

	private async void RootView_Loaded(object sender, RoutedEventArgs e)
	{
		if (_isLoaded)
		{
			return;
		}

		_isLoaded = true;
		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("RootView", "Loaded START");
		Sidebar.SelectedItem ??= ViewModel.HomeNavigationItem;

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

	private async void Sidebar_ItemInvoked(object? sender, ItemInvokedEventArgs args)
	{
		if (!_isLoaded)
		{
			return;
		}

		if (sender is not SidebarItem { Item: NavigationItemViewModel item })
		{
			return;
		}

		try
		{
			await _viewModel.NavigateToNavigationItemAsync(item);
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
