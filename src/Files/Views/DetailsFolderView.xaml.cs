// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;
using Files.Core.ViewSettings;
using Files.Localization;
using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class DetailsFolderView : UserControl
{
	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(FolderBrowserViewModel), typeof(DetailsFolderView), new PropertyMetadata(null, ViewModelChanged));

	public FolderBrowserViewModel? ViewModel
	{
		get => (FolderBrowserViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public DetailsFolderView()
	{
		InitializeComponent();
		ItemTable.SortByText = Strings.SortBy.GetLocalized();
		ItemTable.GroupByText = Strings.GroupBy.GetLocalized();
		ItemTable.AscendingText = Strings.Ascending.GetLocalized();
		ItemTable.DescendingText = Strings.Descending.GetLocalized();
		ItemTable.SortRequested += ItemTable_SortRequested;
		ItemTable.GroupRequested += ItemTable_GroupRequested;
		ItemTable.ColumnWidthChanged += ItemTable_ColumnWidthChanged;
		ItemTable.ColumnReordered += ItemTable_ColumnReordered;
		Loaded += FolderView_Loaded;
		Unloaded += FolderView_Unloaded;
	}

	private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not DetailsFolderView view)
		{
			return;
		}

		if (args.OldValue is FolderBrowserViewModel oldViewModel)
		{
			oldViewModel.PropertyChanged -= view.ViewModel_PropertyChanged;
		}

		if (view.IsLoaded && args.NewValue is FolderBrowserViewModel newViewModel)
		{
			newViewModel.PropertyChanged += view.ViewModel_PropertyChanged;
		}

		view.UpdateSortState();
	}

	private void FolderView_Loaded(object sender, RoutedEventArgs e)
	{
		if (ViewModel is not null)
		{
			ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
			ViewModel.PropertyChanged += ViewModel_PropertyChanged;
		}

		UpdateSortState();
	}

	private void FolderView_Unloaded(object sender, RoutedEventArgs e)
	{
		if (ViewModel is not null)
		{
			ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
		}
	}

	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(FolderBrowserViewModel.ViewSettings))
		{
			UpdateSortState();
		}
	}

	private void UpdateSortState()
	{
		ItemTable.SortColumnId = ViewModel?.ViewSettings.SortPropertyId;
		ItemTable.SortDirection = ViewModel?.ViewSettings.SortDirection switch
		{
			ViewSortDirection.Ascending => TableViewSortDirection.Ascending,
			ViewSortDirection.Descending => TableViewSortDirection.Descending,
			_ => TableViewSortDirection.None,
		};
	}

	private async void ItemTable_SortRequested(object? sender, TableViewColumnOperationRequestedEventArgs e)
	{
		e.Handled = true;
		if (ViewModel is null)
		{
			return;
		}

		try
		{
			await ViewModel.SetSortAsync(e.Column.Id, ToViewSortDirection(e.Direction));
		}
		catch (Exception exception)
		{
			ViewModel.ReportOperationError(exception);
		}
	}

	private async void ItemTable_GroupRequested(object? sender, TableViewColumnOperationRequestedEventArgs e)
	{
		e.Handled = true;
		if (ViewModel is null)
		{
			return;
		}

		try
		{
			await ViewModel.SetGroupingAsync(e.Column.Id, ToViewSortDirection(e.Direction));
		}
		catch (Exception exception)
		{
			ViewModel.ReportOperationError(exception);
		}
	}

	private async void ItemTable_ColumnWidthChanged(object? sender, TableViewColumnWidthChangedEventArgs e)
	{
		await SaveColumnSettingsAsync();
	}

	private async void ItemTable_ColumnReordered(object? sender, TableViewColumnReorderedEventArgs e)
	{
		await SaveColumnSettingsAsync();
	}

	private async Task SaveColumnSettingsAsync()
	{
		if (ViewModel is null)
		{
			return;
		}

		var activeIdentifiers = ItemTable.ActiveColumns.Select(static column => column.Id).ToHashSet(StringComparer.Ordinal);
		var settings = ItemTable.ActiveColumns.Select(static (column, index) => new ViewColumnSettings(column.Id, column.Width, index)).ToList();
		foreach (var existing in ViewModel.ViewSettings.Columns.Where(column => !activeIdentifiers.Contains(column.PropertyId)).OrderBy(static column => column.Order))
		{
			settings.Add(new(existing.PropertyId, existing.Width, settings.Count, existing.IsVisible));
		}

		try
		{
			await ViewModel.SetColumnsAsync(settings);
		}
		catch (Exception exception)
		{
			ViewModel.ReportOperationError(exception);
		}
	}

	private static ViewSortDirection ToViewSortDirection(TableViewSortDirection direction)
	{
		return direction is TableViewSortDirection.Descending ? ViewSortDirection.Descending : ViewSortDirection.Ascending;
	}
}
