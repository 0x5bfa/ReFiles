// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml.Data;

namespace Files.Controls;

/// <summary>
/// Defines the shared behavior of a column displayed by <see cref="TableView"/>.
/// </summary>
public abstract partial class TableViewColumn : FrameworkElement, ITableViewColumn
{
	private WeakReference<TableView>? _owner;

	/// <summary>Gets or sets the property path and stable operation identifier for the column.</summary>
	[GeneratedDependencyProperty]
	public partial string? Binding { get; set; }

	/// <summary>Gets or sets the column header content.</summary>
	[GeneratedDependencyProperty]
	public partial object? Header { get; set; }

	/// <summary>Gets or sets the template used to display the column header.</summary>
	[GeneratedDependencyProperty]
	public partial DataTemplate? HeaderTemplate { get; set; }

	/// <summary>Gets or sets the column width in device-independent pixels.</summary>
	[GeneratedDependencyProperty(DefaultValue = 100d)]
	public partial double ColumnWidth { get; set; }

	/// <summary>Gets or sets the alignment used by text-based cells.</summary>
	[GeneratedDependencyProperty]
	public partial TextAlignment TextAlignment { get; set; }

	/// <summary>Gets or sets a value indicating whether this is the primary column.</summary>
	[GeneratedDependencyProperty]
	public partial bool IsPrimary { get; set; }

	/// <summary>Gets or sets a value indicating whether the user can resize the column.</summary>
	[GeneratedDependencyProperty(DefaultValue = true)]
	public partial bool CanUserResize { get; set; }

	/// <summary>Gets or sets a value indicating whether the user can reorder the column.</summary>
	[GeneratedDependencyProperty(DefaultValue = true)]
	public partial bool CanUserReorder { get; set; }

	/// <summary>Gets or sets a value indicating whether the user can sort by the column.</summary>
	[GeneratedDependencyProperty(DefaultValue = true)]
	public partial bool CanUserSort { get; set; }

	/// <summary>Gets or sets a value indicating whether the user can group by the column.</summary>
	[GeneratedDependencyProperty(DefaultValue = true)]
	public partial bool CanUserGroup { get; set; }

	string ITableViewColumn.Id => Binding ?? string.Empty;

	double ITableViewColumn.Width
	{
		get => ColumnWidth;
		set => ColumnWidth = value;
	}

	bool ITableViewColumn.CanResize => CanUserResize;

	bool ITableViewColumn.CanReorder => CanUserReorder;

	bool ITableViewColumn.CanSort => CanUserSort;

	bool ITableViewColumn.CanGroup => CanUserGroup;

	DataTemplate? ITableViewColumn.CellTemplate => GetCellTemplate();

	/// <inheritdoc />
	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>
	/// Initializes a table column.
	/// </summary>
	protected TableViewColumn()
	{
		MinWidth = 0;
		MaxWidth = 1200;
		RegisterPropertyChangedCallback(MinWidthProperty, ColumnConstraintChanged);
		RegisterPropertyChangedCallback(MaxWidthProperty, ColumnConstraintChanged);
	}

	internal void AttachOwner(TableView owner)
	{
		ArgumentNullException.ThrowIfNull(owner);

		if (_owner is not null && _owner.TryGetTarget(out var currentOwner) && !ReferenceEquals(currentOwner, owner))
		{
			throw new InvalidOperationException($"A {nameof(TableViewColumn)} cannot be shared by multiple {nameof(TableView)} controls.");
		}

		_owner = new(owner);
	}

	internal void DetachOwner(TableView owner)
	{
		if (_owner is not null && _owner.TryGetTarget(out var currentOwner) && ReferenceEquals(currentOwner, owner))
		{
			_owner = null;
		}
	}

	internal FrameworkElement CreateElement(object dataItem)
	{
		ArgumentNullException.ThrowIfNull(dataItem);

		return GenerateElement(dataItem);
	}

	internal bool TryUpdateElement(FrameworkElement element, object dataItem)
	{
		ArgumentNullException.ThrowIfNull(element);

		ArgumentNullException.ThrowIfNull(dataItem);

		return UpdateElement(element, dataItem);
	}

	/// <summary>
	/// Creates the display element for a cell.
	/// </summary>
	/// <param name="dataItem">The row data item.</param>
	/// <returns>The created cell element.</returns>
	protected abstract FrameworkElement GenerateElement(object dataItem);

	/// <summary>
	/// Updates a recyclable cell element.
	/// </summary>
	/// <param name="element">The existing cell element.</param>
	/// <param name="dataItem">The current row data item.</param>
	/// <returns><see langword="true"/> when the existing element was updated.</returns>
	protected abstract bool UpdateElement(FrameworkElement element, object dataItem);

	/// <summary>Gets the template exposed through the column contract.</summary>
	/// <returns>The cell template, or <see langword="null"/>.</returns>
	protected virtual DataTemplate? GetCellTemplate()
	{
		return null;
	}

	/// <summary>Notifies the owning table that realized cells must be refreshed.</summary>
	protected void NotifyCellsChanged()
	{
		RaisePropertyChanged(nameof(ITableViewColumn.CellTemplate));
	}

	partial void OnBindingChanged(string? newValue)
	{
		RaisePropertyChanged(nameof(Binding));
	}

	partial void OnHeaderChanged(object? newValue)
	{
		RaisePropertyChanged(nameof(Header));
	}

	partial void OnHeaderTemplateChanged(DataTemplate? newValue)
	{
		RaisePropertyChanged(nameof(HeaderTemplate));
	}

	partial void OnColumnWidthChanged(double newValue)
	{
		if (!double.IsFinite(newValue) || newValue < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(newValue));
		}

		RaisePropertyChanged(nameof(ITableViewColumn.Width));
	}

	partial void OnTextAlignmentChanged(TextAlignment newValue)
	{
		RaisePropertyChanged(nameof(TextAlignment));
	}

	partial void OnIsPrimaryChanged(bool newValue)
	{
		RaisePropertyChanged(nameof(IsPrimary));
	}

	partial void OnCanUserResizeChanged(bool newValue)
	{
		RaisePropertyChanged(nameof(ITableViewColumn.CanResize));
	}

	partial void OnCanUserReorderChanged(bool newValue)
	{
		RaisePropertyChanged(nameof(ITableViewColumn.CanReorder));
	}

	partial void OnCanUserSortChanged(bool newValue)
	{
		RaisePropertyChanged(nameof(ITableViewColumn.CanSort));
	}

	partial void OnCanUserGroupChanged(bool newValue)
	{
		RaisePropertyChanged(nameof(ITableViewColumn.CanGroup));
	}

	private void ColumnConstraintChanged(DependencyObject sender, DependencyProperty property)
	{
		RaisePropertyChanged(property == MinWidthProperty ? nameof(MinWidth) : nameof(MaxWidth));
	}

	private void RaisePropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new(propertyName));
	}
}

/// <summary>
/// Displays a text value resolved from a row item.
/// </summary>
public partial class TableViewTextColumn : TableViewColumn
{
	/// <summary>Gets or sets the style applied to generated text elements.</summary>
	[GeneratedDependencyProperty]
	public partial Style? ElementStyle { get; set; }

	/// <inheritdoc />
	protected override FrameworkElement GenerateElement(object dataItem)
	{
		var textBlock = new TextBlock
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis,
		};
		UpdateTextBlock(textBlock, dataItem);

		return textBlock;
	}

	/// <inheritdoc />
	protected override bool UpdateElement(FrameworkElement element, object dataItem)
	{
		if (element is not TextBlock textBlock)
		{
			return false;
		}

		UpdateTextBlock(textBlock, dataItem);

		return true;
	}

	partial void OnElementStyleChanged(Style? newValue)
	{
		NotifyCellsChanged();
	}

	private void UpdateTextBlock(TextBlock textBlock, object dataItem)
	{
		textBlock.Style = ElementStyle;
		textBlock.TextAlignment = TextAlignment;
		textBlock.ClearValue(TextBlock.TextProperty);
		var path = Binding;
		if (!string.IsNullOrWhiteSpace(path) && dataItem is ITableViewCellValueProvider provider)
		{
			textBlock.DataContext = null;
			textBlock.Text = provider.GetDisplayText(path);

			return;
		}

		if (!string.IsNullOrWhiteSpace(path))
		{
			textBlock.DataContext = dataItem;
			textBlock.SetBinding(TextBlock.TextProperty, new Binding { Mode = BindingMode.OneWay, Path = new(path) });

			return;
		}

		textBlock.DataContext = null;
		textBlock.Text = dataItem.ToString() ?? string.Empty;
	}
}

/// <summary>
/// Displays date values using the row's display-value provider or a regular binding path.
/// </summary>
public sealed partial class TableViewDateColumn : TableViewTextColumn
{
}

/// <summary>
/// Displays a Boolean value with a read-only check box.
/// </summary>
public sealed partial class TableViewCheckBoxColumn : TableViewColumn
{
	/// <summary>Gets or sets the style applied to generated check boxes.</summary>
	[GeneratedDependencyProperty]
	public partial Style? ElementStyle { get; set; }

	/// <summary>Gets or sets the property path controlling whether the check box is enabled.</summary>
	[GeneratedDependencyProperty]
	public partial string? IsEnabledBinding { get; set; }

	/// <summary>Gets or sets the property path controlling cell visibility.</summary>
	[GeneratedDependencyProperty]
	public partial string? VisibilityBinding { get; set; }

	/// <summary>Gets or sets the converter applied to the visibility binding.</summary>
	[GeneratedDependencyProperty]
	public partial IValueConverter? VisibilityConverter { get; set; }

	/// <summary>Gets or sets a value indicating whether the check box supports an indeterminate state.</summary>
	[GeneratedDependencyProperty]
	public partial bool IsThreeState { get; set; }

	/// <inheritdoc />
	protected override FrameworkElement GenerateElement(object dataItem)
	{
		var checkBox = new CheckBox
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			IsHitTestVisible = false,
			VerticalAlignment = VerticalAlignment.Center,
		};
		UpdateCheckBox(checkBox, dataItem);

		return checkBox;
	}

	/// <inheritdoc />
	protected override bool UpdateElement(FrameworkElement element, object dataItem)
	{
		if (element is not CheckBox checkBox)
		{
			return false;
		}

		UpdateCheckBox(checkBox, dataItem);

		return true;
	}

	partial void OnElementStyleChanged(Style? newValue)
	{
		NotifyCellsChanged();
	}

	partial void OnIsEnabledBindingChanged(string? newValue)
	{
		NotifyCellsChanged();
	}

	partial void OnVisibilityBindingChanged(string? newValue)
	{
		NotifyCellsChanged();
	}

	partial void OnVisibilityConverterChanged(IValueConverter? newValue)
	{
		NotifyCellsChanged();
	}

	partial void OnIsThreeStateChanged(bool newValue)
	{
		NotifyCellsChanged();
	}

	private void UpdateCheckBox(CheckBox checkBox, object dataItem)
	{
		checkBox.DataContext = dataItem;
		checkBox.Style = ElementStyle;
		checkBox.IsThreeState = IsThreeState;
		checkBox.ClearValue(ToggleButton.IsCheckedProperty);
		checkBox.ClearValue(Control.IsEnabledProperty);
		checkBox.ClearValue(UIElement.VisibilityProperty);
		if (!string.IsNullOrWhiteSpace(Binding))
		{
			checkBox.SetBinding(ToggleButton.IsCheckedProperty, CreateBinding(Binding));
		}

		if (!string.IsNullOrWhiteSpace(IsEnabledBinding))
		{
			checkBox.SetBinding(Control.IsEnabledProperty, CreateBinding(IsEnabledBinding));
		}

		if (!string.IsNullOrWhiteSpace(VisibilityBinding))
		{
			checkBox.SetBinding(UIElement.VisibilityProperty, new Binding { Converter = VisibilityConverter, Mode = BindingMode.OneWay, Path = new(VisibilityBinding) });
		}
	}

	private static Binding CreateBinding(string path)
	{
		return new() { Mode = BindingMode.OneWay, Path = new(path) };
	}
}

/// <summary>
/// Displays cells with a data template.
/// </summary>
public sealed partial class TableViewTemplateColumn : TableViewColumn
{
	/// <summary>Gets or sets the cell template.</summary>
	[GeneratedDependencyProperty]
	public partial DataTemplate? CellTemplate { get; set; }

	/// <summary>Gets or sets the selector used to choose a cell template.</summary>
	[GeneratedDependencyProperty]
	public partial DataTemplateSelector? CellTemplateSelector { get; set; }

	/// <inheritdoc />
	protected override FrameworkElement GenerateElement(object dataItem)
	{
		var presenter = new ContentPresenter
		{
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			VerticalContentAlignment = VerticalAlignment.Stretch,
		};
		UpdatePresenter(presenter, dataItem);

		return presenter;
	}

	/// <inheritdoc />
	protected override bool UpdateElement(FrameworkElement element, object dataItem)
	{
		if (element is not ContentPresenter presenter)
		{
			return false;
		}

		UpdatePresenter(presenter, dataItem);

		return true;
	}

	/// <inheritdoc />
	protected override DataTemplate? GetCellTemplate()
	{
		return CellTemplate;
	}

	partial void OnCellTemplateChanged(DataTemplate? newValue)
	{
		NotifyCellsChanged();
	}

	partial void OnCellTemplateSelectorChanged(DataTemplateSelector? newValue)
	{
		NotifyCellsChanged();
	}

	private void UpdatePresenter(ContentPresenter presenter, object dataItem)
	{
		presenter.Content = dataItem;
		presenter.ContentTemplate = CellTemplate;
		presenter.ContentTemplateSelector = CellTemplateSelector;
	}
}
