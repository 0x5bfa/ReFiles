// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;
using Files.Core.Browsing;
using Files.Infrastructure;
using Files.Localization;

namespace Files.ViewModels;

public sealed class NavigationToolbarViewModel : ObservableObject, IDisposable
{
	private FolderBrowserViewModel? _activeFolderBrowser;

	private CancellationTokenSource? _breadcrumbCancellation;

	private int _isDisposed;

	public CommandBindingViewModel ToggleSidebarCommand { get; }

	public CommandBindingViewModel BackCommand { get; }

	public CommandBindingViewModel ForwardCommand { get; }

	public CommandBindingViewModel UpCommand { get; }

	public CommandBindingViewModel HomeCommand { get; }

	public CommandBindingViewModel NavigatePathCommand { get; }

	public CommandBindingViewModel RefreshCommand { get; }

	public ObservableCollection<NavigationToolbarBreadcrumbItem> BreadcrumbItems { get; } = [];

	public string HomeText => Strings.Home.GetLocalized();

	public string PathModeName => Strings.Address.GetLocalized();

	public string PathPlaceholderText => Strings.EnterFolderPath.GetLocalized();

	public string LocationText => _activeFolderBrowser?.LocationText ?? string.Empty;

	internal NavigationToolbarViewModel(
		CommandBindingViewModel toggleSidebarCommand,
		CommandBindingViewModel backCommand,
		CommandBindingViewModel forwardCommand,
		CommandBindingViewModel upCommand,
		CommandBindingViewModel homeCommand,
		CommandBindingViewModel navigatePathCommand,
		CommandBindingViewModel refreshCommand)
	{
		ArgumentNullException.ThrowIfNull(toggleSidebarCommand);
		ArgumentNullException.ThrowIfNull(backCommand);
		ArgumentNullException.ThrowIfNull(forwardCommand);
		ArgumentNullException.ThrowIfNull(upCommand);
		ArgumentNullException.ThrowIfNull(homeCommand);
		ArgumentNullException.ThrowIfNull(navigatePathCommand);
		ArgumentNullException.ThrowIfNull(refreshCommand);

		ToggleSidebarCommand = toggleSidebarCommand;
		BackCommand = backCommand;
		ForwardCommand = forwardCommand;
		UpCommand = upCommand;
		HomeCommand = homeCommand;
		NavigatePathCommand = navigatePathCommand;
		RefreshCommand = refreshCommand;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_activeFolderBrowser?.PropertyChanged -= ActiveFolderBrowser_PropertyChanged;
		_activeFolderBrowser = null;
		_breadcrumbCancellation?.Cancel();
		_breadcrumbCancellation = null;
	}

	internal void SetActiveFolderBrowser(FolderBrowserViewModel? value)
	{
		if (ReferenceEquals(_activeFolderBrowser, value))
		{
			return;
		}

		_activeFolderBrowser?.PropertyChanged -= ActiveFolderBrowser_PropertyChanged;
		_activeFolderBrowser = value;
		_activeFolderBrowser?.PropertyChanged += ActiveFolderBrowser_PropertyChanged;

		OnPropertyChanged(nameof(LocationText));
		_ = RefreshBreadcrumbItemsAsync();
	}

	internal Task NavigateToBreadcrumbAsync(NavigationToolbarBreadcrumbItem item, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(item);

		return _activeFolderBrowser?.NavigateToLocationAsync(item.Location, cancellationToken) ?? Task.CompletedTask;
	}

	internal Task NavigateHomeAsync(CancellationToken cancellationToken = default) =>
		_activeFolderBrowser?.NavigateHomeAsync(cancellationToken) ?? Task.CompletedTask;

	internal Task<IReadOnlyList<NavigationToolbarBreadcrumbItem>> GetBreadcrumbChildrenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		return _activeFolderBrowser?.GetBreadcrumbChildrenAsync(location, cancellationToken) ?? Task.FromResult<IReadOnlyList<NavigationToolbarBreadcrumbItem>>([]);
	}

	private void ActiveFolderBrowser_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is null or nameof(FolderBrowserViewModel.LocationText))
		{
			OnPropertyChanged(nameof(LocationText));
		}

		if (e.PropertyName is null or nameof(FolderBrowserViewModel.Location) or nameof(FolderBrowserViewModel.ShowHiddenItems))
		{
			_ = RefreshBreadcrumbItemsAsync();
		}
	}

	private async Task RefreshBreadcrumbItemsAsync()
	{
		var browser = _activeFolderBrowser;
		_breadcrumbCancellation?.Cancel();
		if (browser is null)
		{
			BreadcrumbItems.Clear();

			return;
		}

		var cancellation = new CancellationTokenSource();
		_breadcrumbCancellation = cancellation;
		try
		{
			var items = await browser.GetBreadcrumbItemsAsync(cancellation.Token);
			if (cancellation.IsCancellationRequested || !ReferenceEquals(_activeFolderBrowser, browser))
			{
				return;
			}

			BreadcrumbItems.Clear();
			foreach (var item in items)
			{
				BreadcrumbItems.Add(item);
			}
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			UiDiagnosticLog.Write("NavigationToolbar", $"Breadcrumb refresh failed: {error.Message}");
			if (ReferenceEquals(_activeFolderBrowser, browser))
			{
				BreadcrumbItems.Clear();
			}
		}
		finally
		{
			if (ReferenceEquals(_breadcrumbCancellation, cancellation))
			{
				_breadcrumbCancellation = null;
			}

			cancellation.Dispose();
		}
	}
}
