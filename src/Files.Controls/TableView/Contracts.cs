// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Input;

namespace Files.Controls;

/// <summary>
/// Describes a read-only column displayed by <see cref="TableView"/>.
/// </summary>
public interface ITableViewColumn : INotifyPropertyChanged
{
	/// <summary>Gets the stable column identifier.</summary>
	string Id { get; }

	/// <summary>Gets the column header content.</summary>
	object? Header { get; }

	/// <summary>Gets the optional template used to display the column header.</summary>
	DataTemplate? HeaderTemplate { get; }

	/// <summary>Gets or sets the column width in device-independent pixels.</summary>
	double Width { get; set; }

	/// <summary>Gets the minimum permitted width.</summary>
	double MinWidth { get; }

	/// <summary>Gets the maximum permitted width.</summary>
	double MaxWidth { get; }

	/// <summary>Gets the text alignment used by the default cell presenter.</summary>
	TextAlignment TextAlignment { get; }

	/// <summary>Gets a value indicating whether this is the primary column.</summary>
	bool IsPrimary { get; }

	/// <summary>Gets a value indicating whether the user can resize the column.</summary>
	bool CanResize { get; }

	/// <summary>Gets a value indicating whether the user can reorder the column.</summary>
	bool CanReorder { get; }

	/// <summary>Gets a value indicating whether the user can sort by the column.</summary>
	bool CanSort { get; }

	/// <summary>Gets a value indicating whether the user can group by the column.</summary>
	bool CanGroup { get; }

	/// <summary>Gets an optional template used to display cells in this column.</summary>
	DataTemplate? CellTemplate { get; }
}

/// <summary>
/// Supplies display text for default <see cref="TableViewRow"/> cells.
/// </summary>
public interface ITableViewCellValueProvider
{
	/// <summary>
	/// Gets display text for a column.
	/// </summary>
	/// <param name="columnId">The stable column identifier.</param>
	/// <returns>The text to display.</returns>
	string GetDisplayText(string columnId);
}

/// <summary>
/// Hosts rows for a <see cref="TableView"/> without exposing a particular items control implementation.
/// </summary>
public interface ITableViewRowsHost
{
	/// <summary>Gets the visual element hosted by the table.</summary>
	FrameworkElement Element { get; }

	/// <summary>Gets or sets the items source.</summary>
	object? ItemsSource { get; set; }

	/// <summary>Gets or sets the row template.</summary>
	DataTemplate? ItemTemplate { get; set; }

	/// <summary>Gets or sets the group header template.</summary>
	DataTemplate? GroupHeaderTemplate { get; set; }

	/// <summary>Gets the current horizontal scroll offset.</summary>
	double HorizontalOffset { get; }

	/// <summary>Gets the current viewport width.</summary>
	double ViewportWidth { get; }

	/// <summary>Occurs when a row is realized or recycled.</summary>
	event EventHandler<TableViewRowChangingEventArgs>? RowChanging;

	/// <summary>Occurs when the viewport dimensions or offset change.</summary>
	event EventHandler? ViewportChanged;

	/// <summary>Scrolls an item into view.</summary>
	/// <param name="item">The item to reveal.</param>
	void ScrollIntoView(object item);
}

/// <summary>
/// Exposes optional row-selection capabilities independently from a rows host implementation.
/// </summary>
public interface ITableViewSelectionHost
{
	/// <summary>Gets the currently focused selection.</summary>
	object? SelectedItem { get; }

	/// <summary>Gets the selected items collection.</summary>
	IList<object> SelectedItems { get; }

	/// <summary>Occurs when the selection changes.</summary>
	event EventHandler? SelectionChanged;

	/// <summary>Occurs when the user invokes the selected item.</summary>
	event EventHandler<TableViewItemInvokedEventArgs>? ItemInvoked;
}

/// <summary>
/// Receives realization data from a <see cref="TableView"/>.
/// </summary>
public interface ITableViewRow
{
	/// <summary>Binds the row to a realized item.</summary>
	/// <param name="binding">The current row binding.</param>
	void Bind(TableViewRowBinding binding);

	/// <summary>Updates only the shared column layout.</summary>
	/// <param name="layout">The current resolved layout.</param>
	void UpdateLayout(TableViewColumnLayout layout);

	/// <summary>Releases references held for a recycled item.</summary>
	void Unbind();
}

/// <summary>
/// Specifies the direction of a requested table operation.
/// </summary>
public enum TableViewSortDirection
{
	/// <summary>No direction is active.</summary>
	None,

	/// <summary>Values are ordered from low to high.</summary>
	Ascending,

	/// <summary>Values are ordered from high to low.</summary>
	Descending,
}

/// <summary>
/// Contains information about a row being realized or recycled.
/// </summary>
public sealed class TableViewRowChangingEventArgs : EventArgs
{
	/// <summary>Gets the source item.</summary>
	public object? Item { get; }

	/// <summary>Gets the root produced by the item template.</summary>
	public object? TemplateRoot { get; }

	/// <summary>Gets the zero-based item index.</summary>
	public int Index { get; }

	/// <summary>Gets the hierarchy depth supplied by the rows host.</summary>
	public int Depth { get; }

	/// <summary>Gets a value indicating whether the row is entering the recycle queue.</summary>
	public bool InRecycleQueue { get; }

	/// <summary>
	/// Initializes row realization information.
	/// </summary>
	/// <param name="item">The source item.</param>
	/// <param name="templateRoot">The item template root.</param>
	/// <param name="index">The item index.</param>
	/// <param name="depth">The hierarchy depth.</param>
	/// <param name="inRecycleQueue">Whether the row is being recycled.</param>
	public TableViewRowChangingEventArgs(object? item, object? templateRoot, int index, int depth, bool inRecycleQueue)
	{
		Item = item;
		TemplateRoot = templateRoot;
		Index = index;
		Depth = depth;
		InRecycleQueue = inRecycleQueue;
	}
}

/// <summary>
/// Contains an item invoked through a rows host.
/// </summary>
public sealed class TableViewItemInvokedEventArgs : EventArgs
{
	/// <summary>Gets the invoked item.</summary>
	public object Item { get; }

	/// <summary>Initializes item invocation information.</summary>
	/// <param name="item">The invoked item.</param>
	public TableViewItemInvokedEventArgs(object item)
	{
		ArgumentNullException.ThrowIfNull(item);

		Item = item;
	}
}

/// <summary>
/// Contains all state required to bind a realized table row.
/// </summary>
public readonly struct TableViewRowBinding
{
	/// <summary>Gets the source item.</summary>
	public object Item { get; }

	/// <summary>Gets the hierarchy depth.</summary>
	public int Depth { get; }

	/// <summary>Gets the active columns.</summary>
	public IReadOnlyList<ITableViewColumn> Columns { get; }

	/// <summary>Gets the resolved column layout.</summary>
	public TableViewColumnLayout Layout { get; }

	/// <summary>Gets the minimum row height.</summary>
	public double RowHeight { get; }

	/// <summary>Gets the indentation applied per hierarchy level.</summary>
	public double Indentation { get; }

	/// <summary>Gets the padding applied inside cells.</summary>
	public Thickness CellPadding { get; }

	/// <summary>
	/// Initializes a row binding.
	/// </summary>
	public TableViewRowBinding(
		object item,
		int depth,
		IReadOnlyList<ITableViewColumn> columns,
		TableViewColumnLayout layout,
		double rowHeight,
		double indentation,
		Thickness cellPadding)
	{
		ArgumentNullException.ThrowIfNull(item);

		ArgumentNullException.ThrowIfNull(columns);

		ArgumentNullException.ThrowIfNull(layout);

		Item = item;
		Depth = depth;
		Columns = columns;
		Layout = layout;
		RowHeight = rowHeight;
		Indentation = indentation;
		CellPadding = cellPadding;
	}
}

/// <summary>
/// Contains a requested sort or group operation.
/// </summary>
public sealed class TableViewColumnOperationRequestedEventArgs : EventArgs
{
	/// <summary>Gets the target column.</summary>
	public ITableViewColumn Column { get; }

	/// <summary>Gets the requested direction.</summary>
	public TableViewSortDirection Direction { get; }

	/// <summary>Gets or sets a value indicating whether the request was handled.</summary>
	public bool Handled { get; set; }

	/// <summary>Initializes a column operation request.</summary>
	public TableViewColumnOperationRequestedEventArgs(ITableViewColumn column, TableViewSortDirection direction)
	{
		ArgumentNullException.ThrowIfNull(column);

		Column = column;
		Direction = direction;
	}
}

/// <summary>
/// Contains a committed column-width change.
/// </summary>
public sealed class TableViewColumnWidthChangedEventArgs : EventArgs
{
	/// <summary>Gets the resized column.</summary>
	public ITableViewColumn Column { get; }

	/// <summary>Gets the width before resizing.</summary>
	public double OldWidth { get; }

	/// <summary>Gets the committed width.</summary>
	public double NewWidth { get; }

	/// <summary>Initializes a column-width change.</summary>
	public TableViewColumnWidthChangedEventArgs(ITableViewColumn column, double oldWidth, double newWidth)
	{
		ArgumentNullException.ThrowIfNull(column);

		Column = column;
		OldWidth = oldWidth;
		NewWidth = newWidth;
	}
}

/// <summary>
/// Contains a committed column reorder.
/// </summary>
public sealed class TableViewColumnReorderedEventArgs : EventArgs
{
	/// <summary>Gets the moved column.</summary>
	public ITableViewColumn Column { get; }

	/// <summary>Gets the previous index.</summary>
	public int OldIndex { get; }

	/// <summary>Gets the new index.</summary>
	public int NewIndex { get; }

	/// <summary>Gets the resulting visible column order.</summary>
	public IReadOnlyList<ITableViewColumn> Columns { get; }

	/// <summary>Initializes a column reorder.</summary>
	public TableViewColumnReorderedEventArgs(ITableViewColumn column, int oldIndex, int newIndex, IReadOnlyList<ITableViewColumn> columns)
	{
		ArgumentNullException.ThrowIfNull(column);

		ArgumentNullException.ThrowIfNull(columns);

		Column = column;
		OldIndex = oldIndex;
		NewIndex = newIndex;
		Columns = columns;
	}
}
