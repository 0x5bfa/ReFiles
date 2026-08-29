// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Microsoft.UI.Xaml.Input;

namespace Files.Controls;

/// <summary>
/// Arranges column headers using the layout shared with table rows.
/// </summary>
public sealed partial class TableViewColumnHeadersPresenter : Panel
{
	private TableView? _owner;

	internal void Attach(TableView owner)
	{
		ArgumentNullException.ThrowIfNull(owner);

		_owner = owner;
		RebuildHeaders();
	}

	internal void Detach(TableView owner)
	{
		if (!ReferenceEquals(_owner, owner))
		{
			return;
		}

		_owner = null;
		Children.Clear();
	}

	internal void RebuildHeaders()
	{
		Children.Clear();
		if (_owner is null)
		{
			return;
		}

		foreach (var column in _owner.ActiveColumns)
		{
			Children.Add(new TableViewColumnHeader(_owner, column));
		}

		InvalidateMeasure();
		InvalidateArrange();
	}

	internal void RefreshHeaders()
	{
		foreach (var header in Children.OfType<TableViewColumnHeader>())
		{
			header.Refresh();
		}

		InvalidateMeasure();
		InvalidateArrange();
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		if (_owner is null)
		{
			return new(0, 0);
		}

		var layout = _owner.ColumnLayout;
		var height = double.IsFinite(availableSize.Height) ? availableSize.Height : _owner.ColumnHeaderHeight;
		var desiredHeight = Math.Max(0, _owner.ColumnHeaderHeight);
		for (var index = 0; index < Children.Count && index < layout.Count; index++)
		{
			Children[index].Measure(new(layout.GetWidth(index), height));
			desiredHeight = Math.Max(desiredHeight, Children[index].DesiredSize.Height);
		}

		return new(layout.ColumnsWidth, desiredHeight);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		if (_owner is null)
		{
			return finalSize;
		}

		var layout = _owner.ColumnLayout;
		for (var index = 0; index < Children.Count && index < layout.Count; index++)
		{
			Children[index].Arrange(new Rect(layout.GetOffset(index), 0, layout.GetWidth(index), finalSize.Height));
		}

		return new(Math.Max(finalSize.Width, layout.ColumnsWidth), finalSize.Height);
	}
}

internal sealed partial class TableViewColumnHeader : Grid
{
	private const string DragDataKey = "Files.Controls.TableViewColumn";

	private readonly TableView _owner;
	private readonly ITableViewColumn _column;
	private readonly Button _button;
	private readonly TextBlock _headerText;
	private readonly ContentPresenter _headerPresenter;
	private readonly FontIcon _sortGlyph;
	private readonly Microsoft.UI.Xaml.Media.RotateTransform _sortTransform;
	private readonly Thumb _resizeThumb;
	private double _resizeStartWidth;

	internal TableViewColumnHeader(TableView owner, ITableViewColumn column)
	{
		_owner = owner;
		_column = column;
		AllowDrop = true;

		_headerText = new TextBlock
		{
			FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
			TextTrimming = TextTrimming.CharacterEllipsis,
			VerticalAlignment = VerticalAlignment.Center,
		};
		_headerPresenter = new ContentPresenter
		{
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			VerticalContentAlignment = VerticalAlignment.Center,
			Visibility = Visibility.Collapsed,
		};
		_sortTransform = new();
		_sortGlyph = new FontIcon
		{
			FontSize = 8,
			Glyph = "\uEDDB",
			Margin = new(4, 0, 0, 0),
			RenderTransform = _sortTransform,
			VerticalAlignment = VerticalAlignment.Center,
			Visibility = Visibility.Collapsed,
		};
		var content = new Grid();
		content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
		content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
		content.Children.Add(_headerText);
		content.Children.Add(_headerPresenter);
		Grid.SetColumn(_sortGlyph, 1);
		content.Children.Add(_sortGlyph);

		_button = new Button
		{
			Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
			BorderThickness = new(0),
			Content = content,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			Padding = new(12, 0, 12, 0),
			VerticalAlignment = VerticalAlignment.Stretch,
		};
		_button.Click += Button_Click;
		_button.DragStarting += Button_DragStarting;
		_button.DropCompleted += Button_DropCompleted;
		Children.Add(_button);

		_resizeThumb = new Thumb
		{
			Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Stretch,
			Width = 8,
		};
		_resizeThumb.DragStarted += ResizeThumb_DragStarted;
		_resizeThumb.DragDelta += ResizeThumb_DragDelta;
		_resizeThumb.DragCompleted += ResizeThumb_DragCompleted;
		Canvas.SetZIndex(_resizeThumb, 1);
		Children.Add(_resizeThumb);

		DragOver += TableViewColumnHeader_DragOver;
		Drop += TableViewColumnHeader_Drop;
		RightTapped += TableViewColumnHeader_RightTapped;
		Refresh();
	}

	internal void Refresh()
	{
		_headerText.Text = _column.Header?.ToString() ?? string.Empty;
		_headerText.Visibility = _column.HeaderTemplate is null ? Visibility.Visible : Visibility.Collapsed;
		_headerPresenter.Content = _column.Header;
		_headerPresenter.ContentTemplate = _column.HeaderTemplate;
		_headerPresenter.Visibility = _column.HeaderTemplate is null ? Visibility.Collapsed : Visibility.Visible;
		_button.CanDrag = _owner.CanUserReorderColumns && _column.CanReorder;
		_resizeThumb.IsEnabled = _owner.CanUserResizeColumns && _column.CanResize;
		_resizeThumb.IsHitTestVisible = _resizeThumb.IsEnabled;

		var isSorted = string.Equals(_owner.SortColumnId, _column.Id, StringComparison.Ordinal) && _owner.SortDirection is not TableViewSortDirection.None;
		_sortGlyph.Visibility = isSorted ? Visibility.Visible : Visibility.Collapsed;
		_sortTransform.Angle = _owner.SortDirection is TableViewSortDirection.Descending ? 180 : 0;
	}

	private void Button_Click(object sender, RoutedEventArgs e)
	{
		_owner.RequestSort(_column);
	}

	private void Button_DragStarting(UIElement sender, DragStartingEventArgs args)
	{
		if (!_owner.BeginColumnDrag(_column))
		{
			args.Cancel = true;

			return;
		}

		args.Data.SetText(_column.Id);
		args.Data.Properties[DragDataKey] = _column.Id;
		args.Data.RequestedOperation = DataPackageOperation.Move;
	}

	private void Button_DropCompleted(UIElement sender, DropCompletedEventArgs args)
	{
		_owner.EndColumnDrag();
	}

	private void TableViewColumnHeader_DragOver(object sender, DragEventArgs e)
	{
		if (_owner.CanDropColumn(_column))
		{
			e.AcceptedOperation = DataPackageOperation.Move;
			e.DragUIOverride.IsCaptionVisible = false;
		}
	}

	private void TableViewColumnHeader_Drop(object sender, DragEventArgs e)
	{
		if (!_owner.CanDropColumn(_column))
		{
			return;
		}

		var insertAfter = e.GetPosition(this).X > ActualWidth / 2;
		_owner.DropColumn(_column, insertAfter);
		e.AcceptedOperation = DataPackageOperation.Move;
	}

	private void TableViewColumnHeader_RightTapped(object sender, RightTappedRoutedEventArgs e)
	{
		_owner.ShowColumnContextMenu(_column, this);
		e.Handled = true;
	}

	private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
	{
		_resizeStartWidth = _column.Width;
		_owner.BeginColumnResize(_column);
	}

	private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
	{
		var delta = FlowDirection is FlowDirection.RightToLeft ? -e.HorizontalChange : e.HorizontalChange;
		_owner.ResizeColumn(_column, delta);
	}

	private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
	{
		_owner.CompleteColumnResize(_column, _resizeStartWidth, e.Canceled);
	}
}
