// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Controls;

/// <summary>
/// Displays read-only tabular rows using a replaceable virtualizing rows host.
/// </summary>
[TemplatePart(Name = PartColumnHeadersPresenter, Type = typeof(TableViewColumnHeadersPresenter))]
[TemplatePart(Name = PartHeaderScrollViewer, Type = typeof(ScrollViewer))]
[TemplatePart(Name = PartRowsHostPresenter, Type = typeof(ContentPresenter))]
public sealed partial class TableView : Control
{
	private const string PartColumnHeadersPresenter = "PART_ColumnHeadersPresenter";
	private const string PartHeaderScrollViewer = "PART_HeaderScrollViewer";
	private const string PartRowsHostPresenter = "PART_RowsHostPresenter";

	private readonly List<ITableViewColumn> _activeColumns = [];
	private readonly Dictionary<ITableViewRow, RealizedRowState> _realizedRows = new(ReferenceEqualityComparer.Instance);
	private TableViewColumnHeadersPresenter? _columnHeadersPresenter;
	private ScrollViewer? _headerScrollViewer;
	private ContentPresenter? _rowsHostPresenter;
	private ITableViewRowsHost? _attachedRowsHost;
	private TableViewColumnLayout _columnLayout = TableViewColumnLayout.Empty;
	private ITableViewColumn? _resizingColumn;
	private ITableViewColumn? _draggedColumn;
	private double _layoutViewportWidth = double.NaN;

	/// <summary>Gets the columns in their current visual order.</summary>
	public IReadOnlyList<ITableViewColumn> ActiveColumns => _activeColumns;

	internal TableViewColumnLayout ColumnLayout => _columnLayout;

	/// <summary>
	/// Initializes a table view.
	/// </summary>
	public TableView()
	{
		DefaultStyleKey = typeof(TableView);
		RowsHost = new ListViewTableRowsHost();
		Loaded += TableView_Loaded;
		Unloaded += TableView_Unloaded;
	}

	protected override void OnApplyTemplate()
	{
		if (_columnHeadersPresenter is not null)
		{
			_columnHeadersPresenter.Detach(this);
		}

		base.OnApplyTemplate();
		_columnHeadersPresenter = GetTemplateChild(PartColumnHeadersPresenter) as TableViewColumnHeadersPresenter;
		_headerScrollViewer = GetTemplateChild(PartHeaderScrollViewer) as ScrollViewer;
		_rowsHostPresenter = GetTemplateChild(PartRowsHostPresenter) as ContentPresenter;
		_columnHeadersPresenter?.Attach(this);
		AttachRowsHost(RowsHost);
		RefreshColumnLayout();
	}

	internal void RequestSort(ITableViewColumn column)
	{
		if (!CanUserSortColumns || !column.CanSort)
		{
			return;
		}

		var direction = string.Equals(SortColumnId, column.Id, StringComparison.Ordinal) && SortDirection is TableViewSortDirection.Ascending
			? TableViewSortDirection.Descending
			: TableViewSortDirection.Ascending;
		var args = new TableViewColumnOperationRequestedEventArgs(column, direction);
		SortRequested?.Invoke(this, args);
		if (!args.Handled)
		{
			SortColumnId = column.Id;
			SortDirection = direction;
		}
	}

	internal void RequestGroup(ITableViewColumn column, TableViewSortDirection direction)
	{
		if (!column.CanGroup || direction is TableViewSortDirection.None)
		{
			return;
		}

		GroupRequested?.Invoke(this, new(column, direction));
	}

	internal void BeginColumnResize(ITableViewColumn column)
	{
		if (CanUserResizeColumns && column.CanResize)
		{
			_resizingColumn = column;
		}
	}

	internal void ResizeColumn(ITableViewColumn column, double delta)
	{
		if (!ReferenceEquals(_resizingColumn, column) || !double.IsFinite(delta))
		{
			return;
		}

		var minimum = double.IsFinite(column.MinWidth) ? Math.Max(0, column.MinWidth) : 0;
		var maximum = double.IsNaN(column.MaxWidth) || column.MaxWidth < minimum ? minimum : column.MaxWidth;
		column.Width = Math.Clamp(column.Width + delta, minimum, maximum);
	}

	internal void CompleteColumnResize(ITableViewColumn column, double oldWidth, bool canceled)
	{
		if (!ReferenceEquals(_resizingColumn, column))
		{
			return;
		}

		_resizingColumn = null;
		if (canceled)
		{
			column.Width = oldWidth;

			return;
		}

		if (!oldWidth.Equals(column.Width))
		{
			ColumnWidthChanged?.Invoke(this, new(column, oldWidth, column.Width));
		}
	}

	internal bool BeginColumnDrag(ITableViewColumn column)
	{
		if (!CanUserReorderColumns || !_activeColumns.Contains(column))
		{
			return false;
		}

		_draggedColumn = column;

		return true;
	}

	internal bool CanDropColumn(ITableViewColumn target)
	{
		return _draggedColumn is not null && !ReferenceEquals(_draggedColumn, target) && _activeColumns.Contains(target);
	}

	internal void DropColumn(ITableViewColumn target, bool insertAfter)
	{
		if (_draggedColumn is not { } draggedColumn || !CanDropColumn(target))
		{
			return;
		}

		var oldIndex = _activeColumns.IndexOf(draggedColumn);
		var targetIndex = _activeColumns.IndexOf(target) + (insertAfter ? 1 : 0);
		_activeColumns.RemoveAt(oldIndex);
		if (oldIndex < targetIndex)
		{
			targetIndex--;
		}

		targetIndex = Math.Clamp(targetIndex, 0, _activeColumns.Count);
		_activeColumns.Insert(targetIndex, draggedColumn);
		_columnHeadersPresenter?.RebuildHeaders();
		RefreshColumnLayout();
		RebindRealizedRows();
		ColumnReordered?.Invoke(this, new(draggedColumn, oldIndex, targetIndex, Array.AsReadOnly(_activeColumns.ToArray())));
	}

	internal void EndColumnDrag()
	{
		_draggedColumn = null;
	}

	internal void ShowColumnContextMenu(ITableViewColumn column, FrameworkElement anchor)
	{
		var flyout = new MenuFlyout();
		if (column.CanSort && !string.IsNullOrWhiteSpace(SortByText))
		{
			flyout.Items.Add(CreateOperationSubMenu(SortByText, direction => RequestSortFromMenu(column, direction)));
		}

		if (column.CanGroup && !string.IsNullOrWhiteSpace(GroupByText))
		{
			flyout.Items.Add(CreateOperationSubMenu(GroupByText, direction => RequestGroup(column, direction)));
		}

		if (flyout.Items.Count is not 0)
		{
			flyout.ShowAt(anchor);
		}
	}

	private void TableView_Loaded(object sender, RoutedEventArgs e)
	{
		if (ColumnsSource is System.Collections.Specialized.INotifyCollectionChanged columnsSource)
		{
			columnsSource.CollectionChanged -= ColumnsSource_CollectionChanged;
			columnsSource.CollectionChanged += ColumnsSource_CollectionChanged;
		}

		SynchronizeColumns();
		AttachRowsHost(RowsHost);
		RefreshColumnLayout();
	}

	private void TableView_Unloaded(object sender, RoutedEventArgs e)
	{
		DetachRowsHost(_attachedRowsHost);
		if (ColumnsSource is System.Collections.Specialized.INotifyCollectionChanged columnsSource)
		{
			columnsSource.CollectionChanged -= ColumnsSource_CollectionChanged;
		}

		UnsubscribeColumns();
	}

	private void ChangeRowsHost(ITableViewRowsHost? oldValue, ITableViewRowsHost? newValue)
	{
		DetachRowsHost(oldValue);
		AttachRowsHost(newValue);
		RefreshColumnLayout();
	}

	private void AttachRowsHost(ITableViewRowsHost? rowsHost)
	{
		if (rowsHost is null)
		{
			return;
		}

		if (ReferenceEquals(rowsHost, _attachedRowsHost))
		{
			if (_rowsHostPresenter is not null)
			{
				_rowsHostPresenter.Content = rowsHost.Element;
			}

			UpdateRowsHostProperties();

			return;
		}

		DetachRowsHost(_attachedRowsHost);
		_attachedRowsHost = rowsHost;
		_attachedRowsHost.RowChanging += RowsHost_RowChanging;
		_attachedRowsHost.ViewportChanged += RowsHost_ViewportChanged;
		if (_rowsHostPresenter is not null)
		{
			_rowsHostPresenter.Content = rowsHost.Element;
		}

		UpdateRowsHostProperties();
	}

	private void DetachRowsHost(ITableViewRowsHost? rowsHost)
	{
		if (rowsHost is null || !ReferenceEquals(rowsHost, _attachedRowsHost))
		{
			return;
		}

		rowsHost.RowChanging -= RowsHost_RowChanging;
		rowsHost.ViewportChanged -= RowsHost_ViewportChanged;
		rowsHost.ItemsSource = null;
		foreach (var row in _realizedRows.Keys)
		{
			row.Unbind();
		}

		_realizedRows.Clear();
		if (_rowsHostPresenter is not null && ReferenceEquals(_rowsHostPresenter.Content, rowsHost.Element))
		{
			_rowsHostPresenter.Content = null;
		}

		_attachedRowsHost = null;
	}

	private void UpdateRowsHostProperties()
	{
		if (_attachedRowsHost is null)
		{
			return;
		}

		_attachedRowsHost.ItemsSource = ItemsSource;
		_attachedRowsHost.ItemTemplate = ItemTemplate;
		_attachedRowsHost.GroupHeaderTemplate = GroupHeaderTemplate;
	}

	private void RowsHost_RowChanging(object? sender, TableViewRowChangingEventArgs e)
	{
		if (e.TemplateRoot is not ITableViewRow row)
		{
			return;
		}

		if (e.InRecycleQueue || e.Item is null)
		{
			row.Unbind();
			_realizedRows.Remove(row);

			return;
		}

		_realizedRows[row] = new(e.Item, e.Depth);
		BindRow(row, e.Item, e.Depth);
	}

	private void RowsHost_ViewportChanged(object? sender, EventArgs e)
	{
		var viewportWidth = GetViewportWidth();
		if (!_layoutViewportWidth.Equals(viewportWidth))
		{
			RefreshColumnLayout();

			return;
		}

		SynchronizeHeaderOffset();
	}

	private void SynchronizeColumns()
	{
		UnsubscribeColumns();
		_activeColumns.Clear();
		if (ColumnsSource is IEnumerable source)
		{
			var identifiers = new HashSet<string>(StringComparer.Ordinal);
			foreach (var item in source)
			{
				if (item is not ITableViewColumn column)
				{
					throw new InvalidOperationException($"{nameof(ColumnsSource)} must contain only {nameof(ITableViewColumn)} instances.");
				}

				if (!identifiers.Add(column.Id))
				{
					throw new InvalidOperationException($"Column identifiers must be unique. Duplicate identifier: {column.Id}");
				}

				_activeColumns.Add(column);
			}
		}

		if (IsLoaded)
		{
			foreach (var column in _activeColumns)
			{
				column.PropertyChanged += Column_PropertyChanged;
			}
		}

		_columnHeadersPresenter?.RebuildHeaders();
		RefreshColumnLayout();
		RebindRealizedRows();
	}

	private void ColumnsSource_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		SynchronizeColumns();
	}

	private void UnsubscribeColumns()
	{
		foreach (var column in _activeColumns)
		{
			column.PropertyChanged -= Column_PropertyChanged;
		}
	}

	private void Column_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(ITableViewColumn.Width) or nameof(ITableViewColumn.MinWidth) or nameof(ITableViewColumn.MaxWidth))
		{
			RefreshColumnLayout();

			return;
		}

		_columnHeadersPresenter?.RefreshHeaders();
		RefreshColumnLayout();
		RebindRealizedRows();
	}

	private void RefreshColumnLayout()
	{
		_layoutViewportWidth = GetViewportWidth();
		_columnLayout = TableViewColumnLayout.Create(_activeColumns, _layoutViewportWidth);
		_columnHeadersPresenter?.InvalidateMeasure();
		_columnHeadersPresenter?.InvalidateArrange();
		SynchronizeHeaderOffset();

		foreach (var row in _realizedRows.Keys)
		{
			row.UpdateLayout(_columnLayout);
		}
	}

	private double GetViewportWidth()
	{
		var width = _attachedRowsHost?.ViewportWidth ?? ActualWidth;

		return double.IsFinite(width) && width >= 0 ? width : 0;
	}

	private void SynchronizeHeaderOffset()
	{
		if (_headerScrollViewer is not null)
		{
			_headerScrollViewer.ChangeView(_attachedRowsHost?.HorizontalOffset ?? 0, null, null, true);
		}
	}

	private void RebindRealizedRows()
	{
		foreach (var realizedRow in _realizedRows)
		{
			BindRow(realizedRow.Key, realizedRow.Value.Item, realizedRow.Value.Depth);
		}
	}

	private void BindRow(ITableViewRow row, object item, int depth)
	{
		row.Bind(new(item, depth, _activeColumns, _columnLayout, PrimaryCellTemplate, Math.Max(0, RowHeight), Math.Max(0, Indentation), CellPadding));
	}

	private MenuFlyoutSubItem CreateOperationSubMenu(string header, Action<TableViewSortDirection> invoke)
	{
		var subMenu = new MenuFlyoutSubItem { Text = header };
		if (!string.IsNullOrWhiteSpace(AscendingText))
		{
			var ascendingItem = new MenuFlyoutItem { Text = AscendingText };
			ascendingItem.Click += (_, _) => invoke(TableViewSortDirection.Ascending);
			subMenu.Items.Add(ascendingItem);
		}

		if (!string.IsNullOrWhiteSpace(DescendingText))
		{
			var descendingItem = new MenuFlyoutItem { Text = DescendingText };
			descendingItem.Click += (_, _) => invoke(TableViewSortDirection.Descending);
			subMenu.Items.Add(descendingItem);
		}

		return subMenu;
	}

	private void RequestSortFromMenu(ITableViewColumn column, TableViewSortDirection direction)
	{
		var args = new TableViewColumnOperationRequestedEventArgs(column, direction);
		SortRequested?.Invoke(this, args);
		if (!args.Handled)
		{
			SortColumnId = column.Id;
			SortDirection = direction;
		}
	}

	private readonly record struct RealizedRowState(object Item, int Depth);
}
