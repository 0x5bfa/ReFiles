// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;
using Files.Controls;
using Files.Core.Storage.Windows;
using Files.Core.ViewSettings;

namespace Files.ViewModels;

public sealed partial class ToolbarViewModel : ObservableObject, IDisposable
{
	private TabViewModel? _activeTab;
	private FolderBrowserViewModel? _activeFolderBrowser;
	private CancellationTokenSource? _layoutSizeCancellation;
	private int _isDisposed;

	public CommandBindingViewModel CopyCommand { get; }

	public CommandBindingViewModel CutCommand { get; }

	public CommandBindingViewModel PasteCommand { get; }

	public CommandBindingViewModel DeleteCommand { get; }

	public CommandBindingViewModel SelectAllCommand { get; }

	public CommandBindingViewModel InvertSelectionCommand { get; }

	public CommandBindingViewModel ClearSelectionCommand { get; }

	public CommandBindingViewModel MountCommand { get; }

	public CommandBindingViewModel BurnDiscImageCommand { get; }

	public CommandBindingViewModel EmptyRecycleBinCommand { get; }

	public CommandBindingViewModel RestoreAllRecycleBinItemsCommand { get; }

	public CommandBindingViewModel RestoreRecycleBinItemsCommand { get; }

	public CommandBindingViewModel CompressToZipCommand { get; }

	public CommandBindingViewModel PinToQuickAccessCommand { get; }

	public CommandBindingViewModel AddToFavoritesCommand { get; }

	public CommandBindingViewModel CopyAsPathCommand { get; }

	public CommandBindingViewModel SortItemsCommand { get; }

	public CommandBindingViewModel GroupItemsCommand { get; }

	public CommandBindingViewModel LayoutDetailsCommand { get; }

	public CommandBindingViewModel LayoutListCommand { get; }

	public CommandBindingViewModel LayoutCardsCommand { get; }

	public CommandBindingViewModel LayoutGridCommand { get; }

	public CommandBindingViewModel LayoutColumnsCommand { get; }

	public bool CanShowNew => _activeFolderBrowser?.CanShowNew is true;

	public ThemedIconData? LayoutIconData => _activeFolderBrowser?.ViewMode switch
	{
		FolderViewMode.List => LayoutListCommand.IconData,
		FolderViewMode.Cards => LayoutCardsCommand.IconData,
		FolderViewMode.Grid => LayoutGridCommand.IconData,
		FolderViewMode.Columns => LayoutColumnsCommand.IconData,
		_ => LayoutDetailsCommand.IconData,
	};

	public double LayoutSize => _activeFolderBrowser?.LayoutSize ?? 3;

	public bool IsLayoutSizeCompact => LayoutSize is 1;

	public bool IsLayoutSizeSmall => LayoutSize is 2;

	public bool IsLayoutSizeMedium => LayoutSize is 3;

	public bool IsLayoutSizeLarge => LayoutSize is 4;

	public bool IsLayoutSizeExtraLarge => LayoutSize is 5;

	public bool IsSortByName => IsPropertySelected(_activeFolderBrowser?.ViewSettings.SortPropertyId, BrowseDisplayPropertyIds.Name, useNameAsDefault: true);

	public bool IsSortByDateModified => IsPropertySelected(_activeFolderBrowser?.ViewSettings.SortPropertyId, BrowseDisplayPropertyIds.DateModified);

	public bool IsSortByDateCreated => IsPropertySelected(_activeFolderBrowser?.ViewSettings.SortPropertyId, BrowseDisplayPropertyIds.DateCreated);

	public bool IsSortBySize => IsPropertySelected(_activeFolderBrowser?.ViewSettings.SortPropertyId, BrowseDisplayPropertyIds.Size);

	public bool IsSortByType => IsPropertySelected(_activeFolderBrowser?.ViewSettings.SortPropertyId, BrowseDisplayPropertyIds.Type);

	public bool IsSortAscending => _activeFolderBrowser?.ViewSettings.SortDirection is not ViewSortDirection.Descending;

	public bool IsSortDescending => _activeFolderBrowser?.ViewSettings.SortDirection is ViewSortDirection.Descending;

	public bool IsGroupByNone => _activeFolderBrowser?.ViewSettings.GroupPropertyId is null;

	public bool IsGroupByName => IsPropertySelected(_activeFolderBrowser?.ViewSettings.GroupPropertyId, BrowseDisplayPropertyIds.Name);

	public bool IsGroupByDateModified => IsPropertySelected(_activeFolderBrowser?.ViewSettings.GroupPropertyId, BrowseDisplayPropertyIds.DateModified);

	public bool IsGroupByDateCreated => IsPropertySelected(_activeFolderBrowser?.ViewSettings.GroupPropertyId, BrowseDisplayPropertyIds.DateCreated);

	public bool IsGroupBySize => IsPropertySelected(_activeFolderBrowser?.ViewSettings.GroupPropertyId, BrowseDisplayPropertyIds.Size);

	public bool IsGroupByType => IsPropertySelected(_activeFolderBrowser?.ViewSettings.GroupPropertyId, BrowseDisplayPropertyIds.Type);

	public bool IsGroupAscending => _activeFolderBrowser?.ViewSettings.GroupDirection is not ViewSortDirection.Descending;

	public bool IsGroupDescending => _activeFolderBrowser?.ViewSettings.GroupDirection is ViewSortDirection.Descending;

	internal ToolbarViewModel(
		CommandBindingViewModel copyCommand,
		CommandBindingViewModel cutCommand,
		CommandBindingViewModel pasteCommand,
		CommandBindingViewModel deleteCommand,
		CommandBindingViewModel selectAllCommand,
		CommandBindingViewModel invertSelectionCommand,
		CommandBindingViewModel clearSelectionCommand,
		CommandBindingViewModel mountCommand,
		CommandBindingViewModel burnDiscImageCommand,
		CommandBindingViewModel emptyRecycleBinCommand,
		CommandBindingViewModel restoreAllRecycleBinItemsCommand,
		CommandBindingViewModel restoreRecycleBinItemsCommand,
		CommandBindingViewModel compressToZipCommand,
		CommandBindingViewModel pinToQuickAccessCommand,
		CommandBindingViewModel addToFavoritesCommand,
		CommandBindingViewModel copyAsPathCommand,
		CommandBindingViewModel sortItemsCommand,
		CommandBindingViewModel groupItemsCommand,
		CommandBindingViewModel layoutDetailsCommand,
		CommandBindingViewModel layoutListCommand,
		CommandBindingViewModel layoutCardsCommand,
		CommandBindingViewModel layoutGridCommand,
		CommandBindingViewModel layoutColumnsCommand)
	{
		ArgumentNullException.ThrowIfNull(copyCommand);
		ArgumentNullException.ThrowIfNull(cutCommand);
		ArgumentNullException.ThrowIfNull(pasteCommand);
		ArgumentNullException.ThrowIfNull(deleteCommand);
		ArgumentNullException.ThrowIfNull(selectAllCommand);
		ArgumentNullException.ThrowIfNull(invertSelectionCommand);
		ArgumentNullException.ThrowIfNull(clearSelectionCommand);
		ArgumentNullException.ThrowIfNull(mountCommand);
		ArgumentNullException.ThrowIfNull(burnDiscImageCommand);
		ArgumentNullException.ThrowIfNull(emptyRecycleBinCommand);
		ArgumentNullException.ThrowIfNull(restoreAllRecycleBinItemsCommand);
		ArgumentNullException.ThrowIfNull(restoreRecycleBinItemsCommand);
		ArgumentNullException.ThrowIfNull(compressToZipCommand);
		ArgumentNullException.ThrowIfNull(pinToQuickAccessCommand);
		ArgumentNullException.ThrowIfNull(addToFavoritesCommand);
		ArgumentNullException.ThrowIfNull(copyAsPathCommand);
		ArgumentNullException.ThrowIfNull(sortItemsCommand);
		ArgumentNullException.ThrowIfNull(groupItemsCommand);
		ArgumentNullException.ThrowIfNull(layoutDetailsCommand);
		ArgumentNullException.ThrowIfNull(layoutListCommand);
		ArgumentNullException.ThrowIfNull(layoutCardsCommand);
		ArgumentNullException.ThrowIfNull(layoutGridCommand);
		ArgumentNullException.ThrowIfNull(layoutColumnsCommand);

		CopyCommand = copyCommand;
		CutCommand = cutCommand;
		PasteCommand = pasteCommand;
		DeleteCommand = deleteCommand;
		SelectAllCommand = selectAllCommand;
		InvertSelectionCommand = invertSelectionCommand;
		ClearSelectionCommand = clearSelectionCommand;
		MountCommand = mountCommand;
		BurnDiscImageCommand = burnDiscImageCommand;
		EmptyRecycleBinCommand = emptyRecycleBinCommand;
		RestoreAllRecycleBinItemsCommand = restoreAllRecycleBinItemsCommand;
		RestoreRecycleBinItemsCommand = restoreRecycleBinItemsCommand;
		CompressToZipCommand = compressToZipCommand;
		PinToQuickAccessCommand = pinToQuickAccessCommand;
		AddToFavoritesCommand = addToFavoritesCommand;
		CopyAsPathCommand = copyAsPathCommand;
		SortItemsCommand = sortItemsCommand;
		GroupItemsCommand = groupItemsCommand;
		LayoutDetailsCommand = layoutDetailsCommand;
		LayoutListCommand = layoutListCommand;
		LayoutCardsCommand = layoutCardsCommand;
		LayoutGridCommand = layoutGridCommand;
		LayoutColumnsCommand = layoutColumnsCommand;
	}

	public void SetLayoutSize(double value)
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || _activeFolderBrowser is not { Location: not null } browser)
		{
			return;
		}

		var cancellation = new CancellationTokenSource();
		Interlocked.Exchange(ref _layoutSizeCancellation, cancellation)?.Cancel();
		_ = SetLayoutSizeAsync(browser, Math.Clamp(Math.Round(value), 1, 5), cancellation);
	}

	public Task<IReadOnlyList<WindowsShellNewItem>> GetNewItemsAsync(CancellationToken cancellationToken = default)
	{
		if (_activeFolderBrowser is not { } browser)
		{
			return Task.FromResult<IReadOnlyList<WindowsShellNewItem>>([]);
		}

		return browser.GetNewItemsAsync(cancellationToken);
	}

	public async Task InvokeNewItemAsync(WindowsShellNewItem item, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(item);

		if (_activeFolderBrowser is not { } browser)
		{
			return;
		}

		try
		{
			await browser.InvokeNewItemAsync(item, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			browser.ReportOperationCanceled();
		}
		catch (Exception exception)
		{
			browser.ReportOperationError(exception);
		}
	}

	public void ReportNewMenuError(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);

		_activeFolderBrowser?.ReportOperationError(exception);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_activeTab?.PropertyChanged -= ActiveTab_PropertyChanged;
		Interlocked.Exchange(ref _layoutSizeCancellation, null)?.Cancel();
		SetActiveFolderBrowser(null);
		_activeTab = null;
	}

	internal void SetActiveTab(TabViewModel? value)
	{
		if (ReferenceEquals(_activeTab, value))
		{
			return;
		}

		_activeTab?.PropertyChanged -= ActiveTab_PropertyChanged;
		_activeTab = value;
		_activeTab?.PropertyChanged += ActiveTab_PropertyChanged;
		SetActiveFolderBrowser(_activeTab?.ActivePane?.FolderBrowser);
	}

	private void ActiveTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is null or nameof(TabViewModel.ActivePane))
		{
			SetActiveFolderBrowser(_activeTab?.ActivePane?.FolderBrowser);
		}
	}

	private void ActiveFolderBrowser_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is null or nameof(FolderBrowserViewModel.CanShowNew))
		{
			OnPropertyChanged(nameof(CanShowNew));
		}

		if (e.PropertyName is null or nameof(FolderBrowserViewModel.ViewMode))
		{
			OnPropertyChanged(nameof(LayoutIconData));
		}

		if (e.PropertyName is null or nameof(FolderBrowserViewModel.ViewSettings))
		{
			RaiseDisplayStateProperties();
			RaiseLayoutSizeProperties();
		}
	}

	private void SetActiveFolderBrowser(FolderBrowserViewModel? browser)
	{
		if (ReferenceEquals(_activeFolderBrowser, browser))
		{
			return;
		}

		if (_activeFolderBrowser is not null)
		{
			_activeFolderBrowser.PropertyChanged -= ActiveFolderBrowser_PropertyChanged;
		}

		_activeFolderBrowser = browser;
		if (_activeFolderBrowser is not null)
		{
			_activeFolderBrowser.PropertyChanged += ActiveFolderBrowser_PropertyChanged;
		}

		OnPropertyChanged(nameof(CanShowNew));
		OnPropertyChanged(nameof(LayoutIconData));
		RaiseLayoutSizeProperties();
		RaiseDisplayStateProperties();
	}

	private async Task SetLayoutSizeAsync(FolderBrowserViewModel browser, double value, CancellationTokenSource cancellation)
	{
		try
		{
			await browser.SetItemSizeAsync(value, cancellation.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			if (Volatile.Read(ref _isDisposed) is 0 && ReferenceEquals(_activeFolderBrowser, browser))
			{
				browser.ReportOperationError(exception);
			}
		}
		finally
		{
			Interlocked.CompareExchange(ref _layoutSizeCancellation, null, cancellation);
			cancellation.Dispose();
		}
	}

	private void RaiseLayoutSizeProperties()
	{
		OnPropertyChanged(nameof(LayoutSize));
		OnPropertyChanged(nameof(IsLayoutSizeCompact));
		OnPropertyChanged(nameof(IsLayoutSizeSmall));
		OnPropertyChanged(nameof(IsLayoutSizeMedium));
		OnPropertyChanged(nameof(IsLayoutSizeLarge));
		OnPropertyChanged(nameof(IsLayoutSizeExtraLarge));
	}

	private void RaiseDisplayStateProperties()
	{
		OnPropertyChanged(nameof(IsSortByName));
		OnPropertyChanged(nameof(IsSortByDateModified));
		OnPropertyChanged(nameof(IsSortByDateCreated));
		OnPropertyChanged(nameof(IsSortBySize));
		OnPropertyChanged(nameof(IsSortByType));
		OnPropertyChanged(nameof(IsSortAscending));
		OnPropertyChanged(nameof(IsSortDescending));
		OnPropertyChanged(nameof(IsGroupByNone));
		OnPropertyChanged(nameof(IsGroupByName));
		OnPropertyChanged(nameof(IsGroupByDateModified));
		OnPropertyChanged(nameof(IsGroupByDateCreated));
		OnPropertyChanged(nameof(IsGroupBySize));
		OnPropertyChanged(nameof(IsGroupByType));
		OnPropertyChanged(nameof(IsGroupAscending));
		OnPropertyChanged(nameof(IsGroupDescending));
	}

	private static bool IsPropertySelected(string? currentPropertyId, string expectedPropertyId, bool useNameAsDefault = false)
	{
		return string.Equals(currentPropertyId, expectedPropertyId, StringComparison.Ordinal) || (useNameAsDefault && currentPropertyId is null);
	}
}
