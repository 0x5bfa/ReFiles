// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.WinUI;

namespace Files.Controls;

/// <summary>
/// Adapts a virtualizing <see cref="ListViewBase"/> for use by <see cref="TableView"/>.
/// </summary>
public class ListViewTableRowsHost : ITableViewRowsHost, ITableViewSelectionHost
{
	private const uint MaximumRowRealizationPhase = 3;

	private ScrollViewer? _scrollViewer;
	private DataTemplate? _groupHeaderTemplate;

	/// <summary>Gets the adapted list control.</summary>
	public ListViewBase View { get; }

	/// <inheritdoc />
	public FrameworkElement Element => View;

	/// <inheritdoc />
	public object? ItemsSource
	{
		get => View.ItemsSource;
		set => View.ItemsSource = value;
	}

	/// <inheritdoc />
	public DataTemplate? ItemTemplate
	{
		get => View.ItemTemplate;
		set => View.ItemTemplate = value;
	}

	/// <inheritdoc />
	public DataTemplate? GroupHeaderTemplate
	{
		get => _groupHeaderTemplate;
		set
		{
			if (_groupHeaderTemplate == value)
			{
				return;
			}

			_groupHeaderTemplate = value;
			UpdateGroupStyle();
		}
	}

	/// <inheritdoc />
	public double HorizontalOffset => _scrollViewer?.HorizontalOffset ?? 0;

	/// <inheritdoc />
	public double ViewportWidth => _scrollViewer?.ViewportWidth ?? View.ActualWidth;

	/// <inheritdoc />
	public object? SelectedItem => View.SelectedItem;

	/// <inheritdoc />
	public IList<object> SelectedItems => View.SelectedItems;

	/// <inheritdoc />
	public event EventHandler<TableViewRowChangingEventArgs>? RowChanging;

	/// <inheritdoc />
	public event EventHandler? ViewportChanged;

	/// <inheritdoc />
	public event EventHandler? SelectionChanged;

	/// <inheritdoc />
	public event EventHandler<TableViewItemInvokedEventArgs>? ItemInvoked;

	/// <summary>
	/// Initializes a host with a new <see cref="ListView"/>.
	/// </summary>
	public ListViewTableRowsHost()
		: this(new ListView())
	{
	}

	/// <summary>
	/// Initializes a host around an existing list control.
	/// </summary>
	/// <param name="view">The list control to adapt.</param>
	public ListViewTableRowsHost(ListViewBase view)
	{
		ArgumentNullException.ThrowIfNull(view);

		View = view;
		View.IsMultiSelectCheckBoxEnabled = false;
		View.SelectionMode = ListViewSelectionMode.Extended;
		View.ShowsScrollingPlaceholders = false;
		ScrollViewer.SetHorizontalScrollMode(View, ScrollMode.Enabled);
		ScrollViewer.SetHorizontalScrollBarVisibility(View, ScrollBarVisibility.Auto);
		View.ContainerContentChanging += View_ContainerContentChanging;
		View.DoubleTapped += View_DoubleTapped;
		View.Loaded += View_Loaded;
		View.SelectionChanged += View_SelectionChanged;
		View.SizeChanged += View_SizeChanged;
	}

	/// <inheritdoc />
	public void ScrollIntoView(object item)
	{
		ArgumentNullException.ThrowIfNull(item);

		View.ScrollIntoView(item);
	}

	/// <summary>
	/// Gets the hierarchy depth for a realized item.
	/// </summary>
	/// <param name="item">The realized item.</param>
	/// <param name="container">The realized item container.</param>
	/// <returns>The zero-based hierarchy depth.</returns>
	protected virtual int GetDepth(object item, DependencyObject container)
	{
		return 0;
	}

	/// <summary>
	/// Gets the row template root from a realized container.
	/// </summary>
	/// <param name="container">The realized item container.</param>
	/// <returns>The template root, or <see langword="null"/> when unavailable.</returns>
	protected virtual object? GetTemplateRoot(DependencyObject container)
	{
		return container switch
		{
			ListViewItem listViewItem => listViewItem.ContentTemplateRoot,
			GridViewItem gridViewItem => gridViewItem.ContentTemplateRoot,
			_ => null,
		};
	}

	private void View_Loaded(object sender, RoutedEventArgs e)
	{
		TryHookScrollViewer();
		EnableStickyGroupHeaders();
	}

	private void View_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
	{
		if (View.SelectedItem is not null)
		{
			ItemInvoked?.Invoke(this, new(View.SelectedItem));
		}
	}

	private void View_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		SelectionChanged?.Invoke(this, EventArgs.Empty);
	}

	private void View_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		TryHookScrollViewer();
		ViewportChanged?.Invoke(this, EventArgs.Empty);
	}

	private void View_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
	{
		if (args.ItemContainer is Control container)
		{
			container.HorizontalAlignment = HorizontalAlignment.Stretch;
			container.HorizontalContentAlignment = HorizontalAlignment.Stretch;
			container.VerticalContentAlignment = VerticalAlignment.Stretch;
		}

		var depth = args.InRecycleQueue || args.Item is null ? 0 : GetDepth(args.Item, args.ItemContainer);
		var templateRoot = GetTemplateRoot(args.ItemContainer);
		if (!args.InRecycleQueue && args.Item is not null && templateRoot is null && args.Phase < MaximumRowRealizationPhase)
		{
			args.RegisterUpdateCallback(args.Phase + 1, View_ContainerContentChanging);

			return;
		}

		RowChanging?.Invoke(this, new(args.Item, templateRoot, args.ItemIndex, depth, args.InRecycleQueue));
	}

	private void TryHookScrollViewer()
	{
		var scrollViewer = View.FindDescendant<ScrollViewer>();
		if (ReferenceEquals(scrollViewer, _scrollViewer))
		{
			return;
		}

		if (_scrollViewer is not null)
		{
			_scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
			_scrollViewer.SizeChanged -= ScrollViewer_SizeChanged;
		}

		_scrollViewer = scrollViewer;
		if (_scrollViewer is not null)
		{
			_scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
			_scrollViewer.SizeChanged += ScrollViewer_SizeChanged;
		}

		ViewportChanged?.Invoke(this, EventArgs.Empty);
	}

	private void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
	{
		ViewportChanged?.Invoke(this, EventArgs.Empty);
	}

	private void ScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		ViewportChanged?.Invoke(this, EventArgs.Empty);
	}

	private void EnableStickyGroupHeaders()
	{
		if (View.ItemsPanelRoot is ItemsStackPanel itemsStackPanel)
		{
			itemsStackPanel.AreStickyGroupHeadersEnabled = true;
		}
	}

	private void UpdateGroupStyle()
	{
		View.GroupStyle.Clear();
		if (_groupHeaderTemplate is not null)
		{
			View.GroupStyle.Add(new GroupStyle { HeaderTemplate = _groupHeaderTemplate });
		}
	}
}
