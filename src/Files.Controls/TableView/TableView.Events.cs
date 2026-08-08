// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Controls;

public sealed partial class TableView
{
	/// <summary>Occurs when the user requests sorting by a column.</summary>
	public event EventHandler<TableViewColumnOperationRequestedEventArgs>? SortRequested;

	/// <summary>Occurs when the user requests grouping by a column.</summary>
	public event EventHandler<TableViewColumnOperationRequestedEventArgs>? GroupRequested;

	/// <summary>Occurs after a column width is committed.</summary>
	public event EventHandler<TableViewColumnWidthChangedEventArgs>? ColumnWidthChanged;

	/// <summary>Occurs after a column is reordered.</summary>
	public event EventHandler<TableViewColumnReorderedEventArgs>? ColumnReordered;
}
