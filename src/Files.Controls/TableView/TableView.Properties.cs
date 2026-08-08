// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.WinUI;
using System.Collections.Specialized;

namespace Files.Controls;

public sealed partial class TableView
{
	/// <summary>Gets or sets the items displayed by the table.</summary>
	[GeneratedDependencyProperty]
	public partial object? ItemsSource { get; set; }

	/// <summary>Gets or sets an enumerable source of <see cref="ITableViewColumn"/> objects.</summary>
	[GeneratedDependencyProperty]
	public partial object? ColumnsSource { get; set; }

	/// <summary>Gets or sets the rows host.</summary>
	[GeneratedDependencyProperty]
	public partial ITableViewRowsHost? RowsHost { get; set; }

	/// <summary>Gets or sets the row template.</summary>
	[GeneratedDependencyProperty]
	public partial DataTemplate? ItemTemplate { get; set; }

	/// <summary>Gets or sets the group header template.</summary>
	[GeneratedDependencyProperty]
	public partial DataTemplate? GroupHeaderTemplate { get; set; }

	/// <summary>Gets or sets the template used by cells in the primary column.</summary>
	[GeneratedDependencyProperty]
	public partial DataTemplate? PrimaryCellTemplate { get; set; }

	/// <summary>Gets or sets the minimum row height.</summary>
	[GeneratedDependencyProperty(DefaultValue = 28d)]
	public partial double RowHeight { get; set; }

	/// <summary>Gets or sets the column header height.</summary>
	[GeneratedDependencyProperty(DefaultValue = 32d)]
	public partial double ColumnHeaderHeight { get; set; }

	/// <summary>Gets or sets the indentation applied for each hierarchy level.</summary>
	[GeneratedDependencyProperty(DefaultValue = 16d)]
	public partial double Indentation { get; set; }

	/// <summary>Gets or sets the padding applied inside cells.</summary>
	[GeneratedDependencyProperty]
	public partial Thickness CellPadding { get; set; }

	/// <summary>Gets or sets a value indicating whether users can resize columns.</summary>
	[GeneratedDependencyProperty(DefaultValue = true)]
	public partial bool CanUserResizeColumns { get; set; }

	/// <summary>Gets or sets a value indicating whether users can reorder columns.</summary>
	[GeneratedDependencyProperty(DefaultValue = true)]
	public partial bool CanUserReorderColumns { get; set; }

	/// <summary>Gets or sets a value indicating whether users can sort columns.</summary>
	[GeneratedDependencyProperty(DefaultValue = true)]
	public partial bool CanUserSortColumns { get; set; }

	/// <summary>Gets or sets the identifier of the sorted column.</summary>
	[GeneratedDependencyProperty]
	public partial string? SortColumnId { get; set; }

	/// <summary>Gets or sets the current sort direction.</summary>
	[GeneratedDependencyProperty]
	public partial TableViewSortDirection SortDirection { get; set; }

	/// <summary>Gets or sets localized text for the sort submenu.</summary>
	[GeneratedDependencyProperty]
	public partial string? SortByText { get; set; }

	/// <summary>Gets or sets localized text for the group submenu.</summary>
	[GeneratedDependencyProperty]
	public partial string? GroupByText { get; set; }

	/// <summary>Gets or sets localized text for ascending operations.</summary>
	[GeneratedDependencyProperty]
	public partial string? AscendingText { get; set; }

	/// <summary>Gets or sets localized text for descending operations.</summary>
	[GeneratedDependencyProperty]
	public partial string? DescendingText { get; set; }

	partial void OnItemsSourceChanged(object? newValue)
	{
		UpdateRowsHostProperties();
	}

	partial void OnColumnsSourcePropertyChanged(DependencyPropertyChangedEventArgs e)
	{
		if (e.OldValue is INotifyCollectionChanged oldSource)
		{
			oldSource.CollectionChanged -= ColumnsSource_CollectionChanged;
		}

		if (IsLoaded && e.NewValue is INotifyCollectionChanged newSource)
		{
			newSource.CollectionChanged += ColumnsSource_CollectionChanged;
		}

		SynchronizeColumns();
	}

	partial void OnRowsHostPropertyChanged(DependencyPropertyChangedEventArgs e)
	{
		ChangeRowsHost(e.OldValue as ITableViewRowsHost, e.NewValue as ITableViewRowsHost);
	}

	partial void OnItemTemplateChanged(DataTemplate? newValue)
	{
		UpdateRowsHostProperties();
	}

	partial void OnGroupHeaderTemplateChanged(DataTemplate? newValue)
	{
		UpdateRowsHostProperties();
	}

	partial void OnPrimaryCellTemplateChanged(DataTemplate? newValue)
	{
		RebindRealizedRows();
	}

	partial void OnRowHeightChanged(double newValue)
	{
		RebindRealizedRows();
	}

	partial void OnColumnHeaderHeightChanged(double newValue)
	{
		_columnHeadersPresenter?.InvalidateMeasure();
	}

	partial void OnIndentationChanged(double newValue)
	{
		RebindRealizedRows();
	}

	partial void OnCellPaddingChanged(Thickness newValue)
	{
		RebindRealizedRows();
	}

	partial void OnCanUserResizeColumnsChanged(bool newValue)
	{
		_columnHeadersPresenter?.RefreshHeaders();
	}

	partial void OnCanUserReorderColumnsChanged(bool newValue)
	{
		_columnHeadersPresenter?.RefreshHeaders();
	}

	partial void OnCanUserSortColumnsChanged(bool newValue)
	{
		_columnHeadersPresenter?.RefreshHeaders();
	}

	partial void OnSortColumnIdChanged(string? newValue)
	{
		_columnHeadersPresenter?.RefreshHeaders();
	}

	partial void OnSortDirectionChanged(TableViewSortDirection newValue)
	{
		_columnHeadersPresenter?.RefreshHeaders();
	}
}
