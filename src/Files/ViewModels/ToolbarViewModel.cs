// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;
using Files.Controls;
using Files.Core.ViewSettings;
using Files.Localization;

namespace Files.ViewModels;

public sealed partial class ToolbarViewModel : ObservableObject, IDisposable
{
	private TabViewModel? _activeTab;
	private FolderBrowserViewModel? _activeFolderBrowser;
	private CancellationTokenSource? _layoutSizeCancellation;
	private int _isDisposed;

	public CommandBindingViewModel NewPaneCommand { get; }

	public CommandBindingViewModel ClosePaneCommand { get; }

	public CommandBindingViewModel SortItemsCommand { get; }

	public CommandBindingViewModel GroupItemsCommand { get; }

	public CommandBindingViewModel LayoutDetailsCommand { get; }

	public CommandBindingViewModel LayoutListCommand { get; }

	public CommandBindingViewModel LayoutCardsCommand { get; }

	public CommandBindingViewModel LayoutGridCommand { get; }

	public CommandBindingViewModel LayoutColumnsCommand { get; }

	public string ActiveTabTitle => _activeTab?.Title ?? Strings.NoTabs.GetLocalized();

	public string SortLabel => Strings.Sort.GetLocalized();

	public string SortByLabel => Strings.SortBy.GetLocalized();

	public string GroupByLabel => Strings.GroupBy.GetLocalized();

	public string NameLabel => Strings.Name.GetLocalized();

	public string DateModifiedLabel => Strings.DateModified.GetLocalized();

	public string DateCreatedLabel => Strings.DateCreated.GetLocalized();

	public string SizeLabel => Strings.Size.GetLocalized();

	public string TypeLabel => Strings.Type.GetLocalized();

	public string NoneLabel => Strings.None.GetLocalized();

	public string AscendingLabel => Strings.Ascending.GetLocalized();

	public string DescendingLabel => Strings.Descending.GetLocalized();

	public string LayoutLabel => Strings.Layout.GetLocalized();

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
		CommandBindingViewModel newPaneCommand,
		CommandBindingViewModel closePaneCommand,
		CommandBindingViewModel sortItemsCommand,
		CommandBindingViewModel groupItemsCommand,
		CommandBindingViewModel layoutDetailsCommand,
		CommandBindingViewModel layoutListCommand,
		CommandBindingViewModel layoutCardsCommand,
		CommandBindingViewModel layoutGridCommand,
		CommandBindingViewModel layoutColumnsCommand)
	{
		ArgumentNullException.ThrowIfNull(newPaneCommand);
		ArgumentNullException.ThrowIfNull(closePaneCommand);
		ArgumentNullException.ThrowIfNull(sortItemsCommand);
		ArgumentNullException.ThrowIfNull(groupItemsCommand);
		ArgumentNullException.ThrowIfNull(layoutDetailsCommand);
		ArgumentNullException.ThrowIfNull(layoutListCommand);
		ArgumentNullException.ThrowIfNull(layoutCardsCommand);
		ArgumentNullException.ThrowIfNull(layoutGridCommand);
		ArgumentNullException.ThrowIfNull(layoutColumnsCommand);

		NewPaneCommand = newPaneCommand;
		ClosePaneCommand = closePaneCommand;
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
		if (Volatile.Read(ref _isDisposed) is not 0 || _activeFolderBrowser is not { } browser)
		{
			return;
		}

		var cancellation = new CancellationTokenSource();
		Interlocked.Exchange(ref _layoutSizeCancellation, cancellation)?.Cancel();
		_ = SetLayoutSizeAsync(browser, Math.Clamp(Math.Round(value), 1, 5), cancellation);
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

		OnPropertyChanged(nameof(ActiveTabTitle));
	}

	private void ActiveTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is null or nameof(TabViewModel.Title) or nameof(TabViewModel.ActivePane))
		{
			OnPropertyChanged(nameof(ActiveTabTitle));
			if (e.PropertyName is null or nameof(TabViewModel.ActivePane))
			{
				SetActiveFolderBrowser(_activeTab?.ActivePane?.FolderBrowser);
			}
		}
	}

	private void ActiveFolderBrowser_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
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
