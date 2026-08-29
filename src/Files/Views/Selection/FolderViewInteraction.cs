// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Commands;
using Files.Controls;
using Files.Core.Browsing;
using Files.Core.Storage.Windows;
using Files.Infrastructure;
using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;
using Windows.System;
using Windows.Win32;

namespace Files.Views.Selection;

/// <summary>
/// Connects a folder view target to its view model while the target is loaded.
/// </summary>
public static class FolderViewInteraction
{
	private static readonly ConditionalWeakTable<FrameworkElement, InteractionRegistration> _registrations = new();

	/// <summary>Identifies the attached view model property.</summary>
	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.RegisterAttached("ViewModel", typeof(FolderBrowserViewModel), typeof(FolderViewInteraction), new PropertyMetadata(null, ViewModelChanged));

	/// <summary>Gets the folder view model connected to an element.</summary>
	/// <param name="element">The folder view target.</param>
	/// <returns>The connected view model, or <see langword="null"/>.</returns>
	public static FolderBrowserViewModel? GetViewModel(DependencyObject element)
	{
		ArgumentNullException.ThrowIfNull(element);

		return (FolderBrowserViewModel?)element.GetValue(ViewModelProperty);
	}

	/// <summary>Sets the folder view model connected to an element.</summary>
	/// <param name="element">The folder view target.</param>
	/// <param name="value">The view model to connect.</param>
	public static void SetViewModel(DependencyObject element, FolderBrowserViewModel? value)
	{
		ArgumentNullException.ThrowIfNull(element);

		element.SetValue(ViewModelProperty, value);
	}

	private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not FrameworkElement target)
		{
			return;
		}

		if (args.NewValue is not FolderBrowserViewModel viewModel)
		{
			if (_registrations.TryGetValue(target, out var existingRegistration))
			{
				existingRegistration.Dispose();
				_registrations.Remove(target);
			}

			return;
		}

		var registration = _registrations.GetValue(target, static element => new InteractionRegistration(element));
		registration.UpdateViewModel(viewModel);
	}

	private sealed class InteractionRegistration : IDisposable
	{
		private readonly FrameworkElement _target;
		private FolderViewInteractionSession? _session;
		private FolderBrowserViewModel? _viewModel;
		private bool _isWaitingForLayout;

		internal InteractionRegistration(FrameworkElement target)
		{
			_target = target;
			_target.Loaded += Target_Loaded;
			_target.Unloaded += Target_Unloaded;
		}

		public void Dispose()
		{
			StopWaitingForLayout();
			DisposeSession();
			_target.Loaded -= Target_Loaded;
			_target.Unloaded -= Target_Unloaded;
		}

		internal void UpdateViewModel(FolderBrowserViewModel viewModel)
		{
			_viewModel = viewModel;
			DisposeSession();
			TryCreateSession();
		}

		private void DisposeSession()
		{
			_session?.Dispose();
			_session = null;
		}

		private void StartWaitingForLayout()
		{
			if (_isWaitingForLayout)
			{
				return;
			}

			_isWaitingForLayout = true;
			_target.LayoutUpdated += Target_LayoutUpdated;
		}

		private void StopWaitingForLayout()
		{
			if (!_isWaitingForLayout)
			{
				return;
			}

			_isWaitingForLayout = false;
			_target.LayoutUpdated -= Target_LayoutUpdated;
		}

		private void Target_LayoutUpdated(object? sender, object e)
		{
			TryCreateSession();
		}

		private void Target_Loaded(object sender, RoutedEventArgs e)
		{
			TryCreateSession();
		}

		private void Target_Unloaded(object sender, RoutedEventArgs e)
		{
			StopWaitingForLayout();
			DisposeSession();
		}

		private void TryCreateSession()
		{
			if (_session is not null || !_target.IsLoaded || _viewModel is null)
			{
				return;
			}

			if (_target is ListViewBase listView)
			{
				_session = new FolderViewInteractionSession(listView, _viewModel);
				StopWaitingForLayout();

				return;
			}

			if (_target is TableView tableView && tableView.RowsHost is ITableViewRowsHost rowsHost && tableView.RowsHost is ITableViewSelectionHost selectionHost)
			{
				_session = new FolderViewInteractionSession(rowsHost, selectionHost, _viewModel);
				StopWaitingForLayout();

				return;
			}

			if (_target is TableView)
			{
				StartWaitingForLayout();
			}
		}
	}
}

internal sealed class FolderViewInteractionSession : IDisposable
{
	private readonly FrameworkElement _element;
	private readonly IList<object> _selectedItems;
	private readonly FolderBrowserViewModel _viewModel;
	private readonly KeyboardAccelerator _propertiesAccelerator = new() { Key = VirtualKey.Enter, Modifiers = VirtualKeyModifiers.Menu };
	private readonly KeyboardAccelerator _selectAllAccelerator = new() { Key = VirtualKey.A, Modifiers = VirtualKeyModifiers.Control };
	private readonly HashSet<int> _realizedIndices = [];
	private readonly ListViewBase? _listView;
	private readonly ListViewBase? _itemsControl;
	private readonly ITableViewRowsHost? _rowsHost;
	private readonly ITableViewSelectionHost? _selectionHost;
	private int _containerContentChangeCount;
	private int _viewportUpdateCount;
	private bool _firstContainerLogged;
	private bool _firstViewportLogged;
	private bool _synchronizingSelection;
	private bool _viewportUpdateQueued;
	private bool _isDisposed;

	internal FolderViewInteractionSession(ListViewBase listView, FolderBrowserViewModel viewModel)
	{
		ArgumentNullException.ThrowIfNull(listView);

		ArgumentNullException.ThrowIfNull(viewModel);

		_listView = listView;
		_itemsControl = listView;
		_element = listView;
		_selectedItems = listView.SelectedItems;
		_viewModel = viewModel;
		UiDiagnosticLog.Write("FolderViewInteraction", $"created control={listView.GetType().Name} items={viewModel.Items.Count}");

		_propertiesAccelerator.Invoked += PropertiesAccelerator_Invoked;
		_element.KeyboardAccelerators.Add(_propertiesAccelerator);
		_selectAllAccelerator.Invoked += SelectAllAccelerator_Invoked;
		_element.KeyboardAccelerators.Add(_selectAllAccelerator);
		listView.DoubleTapped += ListView_DoubleTapped;
		listView.ContextRequested += Element_ContextRequested;
		listView.SelectionChanged += ListView_SelectionChanged;
		listView.ContainerContentChanging += ListView_ContainerContentChanging;
		RectangleSelection.AddSelectionUpdatedHandler(listView, RectangleSelection_SelectionUpdated);
		viewModel.PropertyChanged += ViewModel_PropertyChanged;
		SynchronizeSelection();
	}

	internal FolderViewInteractionSession(ITableViewRowsHost rowsHost, ITableViewSelectionHost selectionHost, FolderBrowserViewModel viewModel)
	{
		ArgumentNullException.ThrowIfNull(rowsHost);

		ArgumentNullException.ThrowIfNull(selectionHost);

		ArgumentNullException.ThrowIfNull(viewModel);

		_rowsHost = rowsHost;
		_selectionHost = selectionHost;
		_element = rowsHost.Element;
		_itemsControl = rowsHost.Element as ListViewBase;
		_selectedItems = selectionHost.SelectedItems;
		_viewModel = viewModel;
		UiDiagnosticLog.Write("FolderViewInteraction", $"created control={rowsHost.Element.GetType().Name} items={viewModel.Items.Count}");

		_propertiesAccelerator.Invoked += PropertiesAccelerator_Invoked;
		_element.KeyboardAccelerators.Add(_propertiesAccelerator);
		_selectAllAccelerator.Invoked += SelectAllAccelerator_Invoked;
		_element.KeyboardAccelerators.Add(_selectAllAccelerator);
		rowsHost.RowChanging += RowsHost_RowChanging;
		selectionHost.ItemInvoked += SelectionHost_ItemInvoked;
		selectionHost.SelectionChanged += SelectionHost_SelectionChanged;
		_element.ContextRequested += Element_ContextRequested;
		if (_itemsControl is not null)
		{
			RectangleSelection.AddSelectionUpdatedHandler(_itemsControl, RectangleSelection_SelectionUpdated);
		}

		viewModel.PropertyChanged += ViewModel_PropertyChanged;
		SynchronizeSelection();
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		if (_itemsControl is not null)
		{
			RectangleSelection.RemoveSelectionUpdatedHandler(_itemsControl, RectangleSelection_SelectionUpdated);
		}

		if (_listView is not null)
		{
			_listView.DoubleTapped -= ListView_DoubleTapped;
			_listView.ContextRequested -= Element_ContextRequested;
			_listView.SelectionChanged -= ListView_SelectionChanged;
			_listView.ContainerContentChanging -= ListView_ContainerContentChanging;
		}

		if (_rowsHost is not null)
		{
			_rowsHost.RowChanging -= RowsHost_RowChanging;
			_element.ContextRequested -= Element_ContextRequested;
		}

		if (_selectionHost is not null)
		{
			_selectionHost.ItemInvoked -= SelectionHost_ItemInvoked;
			_selectionHost.SelectionChanged -= SelectionHost_SelectionChanged;
		}

		_viewModel.PropertyChanged -= ViewModel_PropertyChanged;
		_propertiesAccelerator.Invoked -= PropertiesAccelerator_Invoked;
		_element.KeyboardAccelerators.Remove(_propertiesAccelerator);
		_selectAllAccelerator.Invoked -= SelectAllAccelerator_Invoked;
		_element.KeyboardAccelerators.Remove(_selectAllAccelerator);
		_realizedIndices.Clear();
		UiDiagnosticLog.Write("FolderViewInteraction", $"disposed containers={_containerContentChangeCount} viewportUpdates={_viewportUpdateCount}");
	}

	private async void ListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
	{
		if (_listView is null || e.OriginalSource is not DependencyObject source || FindInvokedListItem(source) is not { } item)
		{
			return;
		}

		e.Handled = true;
		await _viewModel.CommandManager.ExecuteAsync(CommandIds.OpenItem, new OpenItemCommandParameter(item, GetInvocationPoint()));
	}

	private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_itemsControl is null || !RectangleSelection.GetIsUpdatingSelection(_itemsControl))
		{
			UpdateViewModelSelection();
		}
	}

	private void ListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
	{
		TrackRealizedRow(args.ItemIndex, args.InRecycleQueue);
	}

	private async void SelectionHost_ItemInvoked(object? sender, TableViewItemInvokedEventArgs e)
	{
		if (e.Item is BrowseItemViewModel item)
		{
			await _viewModel.CommandManager.ExecuteAsync(CommandIds.OpenItem, new OpenItemCommandParameter(item, GetInvocationPoint()));
		}
	}

	private void SelectionHost_SelectionChanged(object? sender, EventArgs e)
	{
		if (_itemsControl is null || !RectangleSelection.GetIsUpdatingSelection(_itemsControl))
		{
			UpdateViewModelSelection();
		}
	}

	private void RectangleSelection_SelectionUpdated(object? sender, EventArgs e)
	{
		UpdateViewModelSelection();
	}

	private void RowsHost_RowChanging(object? sender, TableViewRowChangingEventArgs e)
	{
		TrackRealizedRow(e.Index, e.InRecycleQueue);
	}

	private async void PropertiesAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		args.Handled = true;
		await _viewModel.CommandManager.ExecuteAsync(CommandIds.Properties);
	}

	private async void SelectAllAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		if (!_viewModel.CommandManager.CanExecute(CommandIds.SelectAll, null))
		{
			return;
		}

		args.Handled = true;
		await _viewModel.CommandManager.ExecuteAsync(CommandIds.SelectAll);
	}

	private void Element_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
	{
		var invokedItem = e.OriginalSource is DependencyObject source ? FindInvokedListItem(source) : null;
		invokedItem ??= _selectedItems.OfType<BrowseItemViewModel>().FirstOrDefault();
		if (invokedItem is null)
		{
			return;
		}

		EnsureContextSelection(invokedItem);
		var selection = _selectedItems.OfType<BrowseItemViewModel>().ToArray();
		if (selection.Length is 0)
		{
			return;
		}

		e.Handled = true;
		var hasPosition = e.TryGetPosition(_element, out var position);
		var anchor = hasPosition ? _element : _itemsControl?.ContainerFromItem(invokedItem) as FrameworkElement ?? _element;
		new ItemContextMenu(_viewModel, invokedItem, selection).ShowAt(anchor, hasPosition ? position : null);
	}

	private void EnsureContextSelection(BrowseItemViewModel invokedItem)
	{
		if (_selectedItems.Contains(invokedItem))
		{
			return;
		}

		if (_itemsControl is not null)
		{
			_itemsControl.SelectedItems.Clear();
			_itemsControl.SelectedItem = invokedItem;
		}
	}

	private void UpdateViewModelSelection()
	{
		if (_isDisposed || !_element.IsLoaded || _synchronizingSelection || _viewModel.IsApplyingUpdate)
		{
			return;
		}

		_viewModel.SetSelection(_selectedItems.OfType<BrowseItemViewModel>());
	}

	private void TrackRealizedRow(int index, bool inRecycleQueue)
	{
		var eventCount = Interlocked.Increment(ref _containerContentChangeCount);
		if (eventCount <= 10 || eventCount % 100 is 0)
		{
			UiDiagnosticLog.Write("FolderViewInteraction", $"ContainerContentChanging count={eventCount} index={index} recycled={inRecycleQueue} realizedBefore={_realizedIndices.Count}");
		}

		if (!_firstContainerLogged && !inRecycleQueue)
		{
			_firstContainerLogged = true;
			UiDiagnosticLog.Write("FolderViewInteraction", $"First container realized index={index}");
		}

		if (inRecycleQueue)
		{
			_realizedIndices.Remove(index);
		}
		else
		{
			_realizedIndices.Add(index);
		}

		QueueViewportUpdate();
	}

	private void QueueViewportUpdate()
	{
		if (_viewportUpdateQueued || _isDisposed)
		{
			return;
		}

		_viewportUpdateQueued = true;
		if (!_element.DispatcherQueue.TryEnqueue(UpdateViewport))
		{
			_viewportUpdateQueued = false;
		}
	}

	private void UpdateViewport()
	{
		_viewportUpdateQueued = false;
		if (_isDisposed)
		{
			return;
		}

		var updateCount = Interlocked.Increment(ref _viewportUpdateCount);
		UiDiagnosticLog.Write("FolderViewInteraction", $"UpdateViewport count={updateCount} realized={_realizedIndices.Count} items={_viewModel.Items.Count}");

		if (_realizedIndices.Count is 0)
		{
			_viewModel.UpdateViewport(new BrowseViewport(0, 0, dpi: GetDpi()));

			return;
		}

		var firstIndex = _realizedIndices.Min();
		var lastIndex = _realizedIndices.Max();
		_viewModel.UpdateViewport(new BrowseViewport(firstIndex, lastIndex - firstIndex + 1, dpi: GetDpi()));
		if (!_firstViewportLogged && _viewModel.Items.Count is not 0)
		{
			_firstViewportLogged = true;
			UiDiagnosticLog.Write("FolderViewInteraction", $"First viewport populated first={firstIndex} count={lastIndex - firstIndex + 1}");
		}
	}

	private int GetDpi()
	{
		var scale = _element.XamlRoot?.RasterizationScale ?? 1.0;

		return Math.Max(1, (int)Math.Round(scale * 96.0));
	}

	private static WindowsShellInvocationPoint GetInvocationPoint()
	{
		var position = PInvoke.GetMessagePos();
		var x = unchecked((short)(position & ushort.MaxValue));
		var y = unchecked((short)(position >> 16));

		return new WindowsShellInvocationPoint(x, y);
	}

	private BrowseItemViewModel? FindInvokedListItem(DependencyObject source)
	{
		for (var current = source; current is not null && current != _itemsControl; current = VisualTreeHelper.GetParent(current))
		{
			if (current is SelectorItem { Content: BrowseItemViewModel item } && _itemsControl?.IndexFromContainer(current) >= 0)
			{
				return item;
			}
		}

		return null;
	}

	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(FolderBrowserViewModel.SelectedKeys))
		{
			SynchronizeSelection();
		}
	}

	private void SynchronizeSelection()
	{
		_synchronizingSelection = true;
		try
		{
			var selectedKeys = _viewModel.SelectedKeys.ToHashSet();
			_selectedItems.Clear();
			foreach (var item in _viewModel.Items)
			{
				if (selectedKeys.Contains(item.Reference.GetKey()))
				{
					_selectedItems.Add(item);
				}
			}
		}
		finally
		{
			_synchronizingSelection = false;
		}
	}
}
