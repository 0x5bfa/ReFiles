// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Foundation;

namespace Files.Controls;

/// <summary>
/// Displays one recyclable read-only table row.
/// </summary>
public sealed partial class TableViewRow : Panel, ITableViewRow
{
	private readonly Dictionary<ITableViewColumn, FrameworkElement> _cells = new(ReferenceEqualityComparer.Instance);
	private IReadOnlyList<ITableViewColumn> _columns = Array.Empty<ITableViewColumn>();
	private TableViewColumnLayout _layout = TableViewColumnLayout.Empty;
	private object? _item;
	private INotifyPropertyChanged? _observableItem;
	private Thickness _cellPadding;
	private double _rowHeight;
	private double _indentation;
	private int _depth;

	/// <summary>
	/// Initializes a table row.
	/// </summary>
	public TableViewRow()
	{
		HorizontalAlignment = HorizontalAlignment.Stretch;
		VerticalAlignment = VerticalAlignment.Stretch;
		Unloaded += TableViewRow_Unloaded;
	}

	/// <inheritdoc />
	public void Bind(TableViewRowBinding binding)
	{
		SetItem(binding.Item);
		_columns = binding.Columns;
		_layout = binding.Layout;
		_cellPadding = binding.CellPadding;
		_rowHeight = binding.RowHeight;
		_indentation = binding.Indentation;
		_depth = binding.Depth;
		MinHeight = _rowHeight;

		ReconcileCells();
		RefreshCells();
		InvalidateMeasure();
		InvalidateArrange();
	}

	/// <inheritdoc />
	public void UpdateLayout(TableViewColumnLayout layout)
	{
		ArgumentNullException.ThrowIfNull(layout);

		_layout = layout;
		InvalidateMeasure();
		InvalidateArrange();
	}

	/// <inheritdoc />
	public void Unbind()
	{
		SetItem(null);
		foreach (var cell in _cells.Values)
		{
			cell.DataContext = null;
			if (cell is TextBlock textBlock)
			{
				textBlock.ClearValue(TextBlock.TextProperty);
			}
			else if (cell is ContentPresenter presenter)
			{
				presenter.Content = null;
			}
		}
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		var availableHeight = double.IsFinite(availableSize.Height) ? availableSize.Height : double.PositiveInfinity;
		var desiredHeight = _rowHeight;
		for (var index = 0; index < _columns.Count && index < _layout.Count; index++)
		{
			if (!_cells.TryGetValue(_columns[index], out var cell))
			{
				continue;
			}

			var indentation = _columns[index].IsPrimary ? Math.Max(0, _depth * _indentation) : 0;
			cell.Measure(new(Math.Max(0, _layout.GetWidth(index) - indentation), availableHeight));
			desiredHeight = Math.Max(desiredHeight, cell.DesiredSize.Height);
		}

		return new(_layout.ColumnsWidth, desiredHeight);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		for (var index = 0; index < _columns.Count && index < _layout.Count; index++)
		{
			var column = _columns[index];
			if (!_cells.TryGetValue(column, out var cell))
			{
				continue;
			}

			var indentation = column.IsPrimary ? Math.Max(0, _depth * _indentation) : 0;
			var width = Math.Max(0, _layout.GetWidth(index) - indentation);
			cell.Arrange(new Rect(_layout.GetOffset(index) + indentation, 0, width, finalSize.Height));
		}

		return new(Math.Max(finalSize.Width, _layout.ColumnsWidth), finalSize.Height);
	}

	private void TableViewRow_Unloaded(object sender, RoutedEventArgs e)
	{
		Unbind();
	}

	private void SetItem(object? item)
	{
		if (ReferenceEquals(_item, item))
		{
			return;
		}

		if (_observableItem is not null)
		{
			_observableItem.PropertyChanged -= Item_PropertyChanged;
		}

		_item = item;
		_observableItem = item as INotifyPropertyChanged;
		if (_observableItem is not null)
		{
			_observableItem.PropertyChanged += Item_PropertyChanged;
		}
	}

	private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		RefreshCells();
	}

	private void ReconcileCells()
	{
		var desiredColumns = _columns.ToHashSet(ReferenceEqualityComparer.Instance);
		foreach (var staleColumn in _cells.Keys.Where(column => !desiredColumns.Contains(column)).ToArray())
		{
			Children.Remove(_cells[staleColumn]);
			_cells.Remove(staleColumn);
		}

		for (var index = 0; index < _columns.Count; index++)
		{
			var column = _columns[index];
			var template = column.CellTemplate;
			if (_cells.TryGetValue(column, out var existingCell) && (column is TableViewColumn || CellUsesTemplate(existingCell, template)))
			{
				MoveCell(existingCell, index);
				continue;
			}

			if (existingCell is not null)
			{
				Children.Remove(existingCell);
			}

			var cell = CreateCell(column, template);
			Children.Insert(Math.Min(index, Children.Count), cell);
			_cells[column] = cell;
		}
	}

	private void RefreshCells()
	{
		foreach (var column in _columns)
		{
			if (!_cells.TryGetValue(column, out var cell))
			{
				continue;
			}

			if (_item is not null && column is TableViewColumn tableColumn)
			{
				if (!tableColumn.TryUpdateElement(cell, _item))
				{
					var index = Children.IndexOf(cell);
					Children.RemoveAt(index);
					cell = tableColumn.CreateElement(_item);
					Children.Insert(index, cell);
					_cells[column] = cell;
				}
			}
			else if (cell is ContentPresenter presenter)
			{
				presenter.Content = _item;
			}
			else if (cell is TextBlock textBlock)
			{
				textBlock.Text = _item is ITableViewCellValueProvider provider ? provider.GetDisplayText(column.Id) : _item?.ToString() ?? string.Empty;
				textBlock.TextAlignment = column.TextAlignment;
			}

			ApplyCellPadding(cell);
		}
	}

	private FrameworkElement CreateCell(ITableViewColumn column, DataTemplate? template)
	{
		if (_item is not null && column is TableViewColumn tableColumn)
		{
			return tableColumn.CreateElement(_item);
		}

		if (template is not null)
		{
			return new ContentPresenter
			{
				ContentTemplate = template,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				VerticalContentAlignment = VerticalAlignment.Stretch,
			};
		}

		return new TextBlock
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Center,
			TextAlignment = column.TextAlignment,
			TextTrimming = TextTrimming.CharacterEllipsis,
		};
	}

	private void ApplyCellPadding(FrameworkElement cell)
	{
		if (cell is ContentPresenter presenter)
		{
			presenter.Padding = _cellPadding;

			return;
		}

		cell.Margin = _cellPadding;
	}

	private void MoveCell(FrameworkElement cell, int targetIndex)
	{
		var currentIndex = Children.IndexOf(cell);
		if (currentIndex == targetIndex)
		{
			return;
		}

		Children.RemoveAt(currentIndex);
		Children.Insert(Math.Min(targetIndex, Children.Count), cell);
	}

	private static bool CellUsesTemplate(FrameworkElement cell, DataTemplate? template)
	{
		return template is null ? cell is TextBlock : cell is ContentPresenter presenter && presenter.ContentTemplate == template;
	}

}
