// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Files.Views;

/// <summary>
/// Builds one details view row from the active Shell column definitions.
/// </summary>
public sealed class DetailsRow : Grid
{
	public static readonly DependencyProperty ItemProperty =
		DependencyProperty.Register(nameof(Item), typeof(BrowseItemViewModel), typeof(DetailsRow), new PropertyMetadata(null, ItemChanged));

	public static readonly DependencyProperty ColumnsProperty =
		DependencyProperty.Register(nameof(Columns), typeof(IReadOnlyList<DetailsColumnViewModel>), typeof(DetailsRow), new PropertyMetadata(null, ColumnsChanged));

	private Image? _thumbnailImage;
	private readonly Dictionary<string, TextBlock> _cells = [];

	public BrowseItemViewModel? Item
	{
		get => (BrowseItemViewModel?)GetValue(ItemProperty);
		set => SetValue(ItemProperty, value);
	}

	public IReadOnlyList<DetailsColumnViewModel>? Columns
	{
		get => (IReadOnlyList<DetailsColumnViewModel>?)GetValue(ColumnsProperty);
		set => SetValue(ColumnsProperty, value);
	}

	public DetailsRow()
	{
		ColumnSpacing = 12;
		HorizontalAlignment = HorizontalAlignment.Stretch;
	}

	private static void ItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not DetailsRow row)
		{
			return;
		}

		if (args.OldValue is BrowseItemViewModel oldItem)
		{
			oldItem.PropertyChanged -= row.Item_PropertyChanged;
		}

		if (args.NewValue is BrowseItemViewModel newItem)
		{
			newItem.PropertyChanged += row.Item_PropertyChanged;
		}

		row.Rebuild();
	}

	private static void ColumnsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is DetailsRow row)
		{
			row.Rebuild();
		}
	}

	private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs args)
	{
		switch (args.PropertyName)
		{
			case nameof(BrowseItemViewModel.Thumbnail):
				if (_thumbnailImage is not null)
				{
					_thumbnailImage.Source = Item?.Thumbnail;
				}

				break;
			case nameof(BrowseItemViewModel.Properties):
				UpdateCellTexts();
				break;
		}
	}

	private void Rebuild()
	{
		ColumnDefinitions.Clear();
		Children.Clear();
		_thumbnailImage = null;
		_cells.Clear();

		if (Item is not { } item || Columns is not { Count: > 0 } columns)
		{
			return;
		}

		ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
		var iconContainer = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
		iconContainer.Children.Add(new FontIcon { FontSize = 16, Glyph = "\uE8B7", Opacity = 0.45, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
		_thumbnailImage = new Image { Source = item.Thumbnail, Stretch = Stretch.Uniform };
		iconContainer.Children.Add(_thumbnailImage);
		Children.Add(iconContainer);

		for (var index = 0; index < columns.Count; index++)
		{
			var column = columns[index];
			ColumnDefinitions.Add(new ColumnDefinition { Width = column.IsStretch ? new GridLength(1, GridUnitType.Star) : new GridLength(column.Width) });
			var textBlock = new TextBlock
			{
				Text = item.GetDisplayText(column.PropertyId),
				TextTrimming = TextTrimming.CharacterEllipsis,
				VerticalAlignment = VerticalAlignment.Center,
				TextAlignment = column.Alignment switch
				{
					Files.Core.Storage.Windows.WindowsShellColumnAlignment.Right => TextAlignment.Right,
					Files.Core.Storage.Windows.WindowsShellColumnAlignment.Center => TextAlignment.Center,
					_ => TextAlignment.Left,
				},
			};
			Grid.SetColumn(textBlock, index + 1);
			Children.Add(textBlock);
			_cells[column.PropertyId] = textBlock;
		}
	}

	private void UpdateCellTexts()
	{
		if (Item is not { } item)
		{
			return;
		}

		foreach (var cell in _cells)
		{
			cell.Value.Text = item.GetDisplayText(cell.Key);
		}
	}
}
