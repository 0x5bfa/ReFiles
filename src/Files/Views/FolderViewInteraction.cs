// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Commands;
using Files.Controls;
using Files.Infrastructure;
using Files.ViewModels;
using Files.Core.Browsing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Files.Views;

internal sealed class FolderViewInteraction : IDisposable
{
	private readonly FrameworkElement _element;
	private readonly IList<object> _selectedItems;
	private readonly FolderBrowserViewModel _viewModel;
	private readonly HashSet<int> _realizedIndices = [];
	private readonly ListViewBase? _listView;
	private readonly ITableViewRowsHost? _rowsHost;
	private readonly ITableViewSelectionHost? _selectionHost;
	private int _containerContentChangeCount;
	private int _viewportUpdateCount;
	private bool _firstContainerLogged;
	private bool _firstViewportLogged;
	private bool _synchronizingSelection;
	private bool _viewportUpdateQueued;
	private bool _isDisposed;

	public FolderViewInteraction(ListViewBase listView, FolderBrowserViewModel viewModel)
	{
		ArgumentNullException.ThrowIfNull(listView);

		ArgumentNullException.ThrowIfNull(viewModel);

		_listView = listView;
		_element = listView;
		_selectedItems = listView.SelectedItems;
		_viewModel = viewModel;
		UiDiagnosticLog.Write("FolderViewInteraction", $"created control={listView.GetType().Name} items={viewModel.Items.Count}");

		listView.DoubleTapped += ListView_DoubleTapped;
		listView.SelectionChanged += ListView_SelectionChanged;
		listView.ContainerContentChanging += ListView_ContainerContentChanging;
		viewModel.PropertyChanged += ViewModel_PropertyChanged;
		SynchronizeSelection();
	}

	public FolderViewInteraction(ITableViewRowsHost rowsHost, ITableViewSelectionHost selectionHost, FolderBrowserViewModel viewModel)
	{
		ArgumentNullException.ThrowIfNull(rowsHost);

		ArgumentNullException.ThrowIfNull(selectionHost);

		ArgumentNullException.ThrowIfNull(viewModel);

		_rowsHost = rowsHost;
		_selectionHost = selectionHost;
		_element = rowsHost.Element;
		_selectedItems = selectionHost.SelectedItems;
		_viewModel = viewModel;
		UiDiagnosticLog.Write("FolderViewInteraction", $"created control={rowsHost.Element.GetType().Name} items={viewModel.Items.Count}");

		rowsHost.RowChanging += RowsHost_RowChanging;
		selectionHost.ItemInvoked += SelectionHost_ItemInvoked;
		selectionHost.SelectionChanged += SelectionHost_SelectionChanged;
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
		if (_listView is not null)
		{
			_listView.DoubleTapped -= ListView_DoubleTapped;
			_listView.SelectionChanged -= ListView_SelectionChanged;
			_listView.ContainerContentChanging -= ListView_ContainerContentChanging;
		}

		if (_rowsHost is not null)
		{
			_rowsHost.RowChanging -= RowsHost_RowChanging;
		}

		if (_selectionHost is not null)
		{
			_selectionHost.ItemInvoked -= SelectionHost_ItemInvoked;
			_selectionHost.SelectionChanged -= SelectionHost_SelectionChanged;
		}

		_viewModel.PropertyChanged -= ViewModel_PropertyChanged;
		_realizedIndices.Clear();
		UiDiagnosticLog.Write("FolderViewInteraction", $"disposed containers={_containerContentChangeCount} viewportUpdates={_viewportUpdateCount}");
	}

	private async void ListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
	{
		if (_listView?.SelectedItem is not BrowseItemViewModel item)
		{
			return;
		}

		await _viewModel.CommandManager.ExecuteAsync(CommandIds.OpenItem, item);
	}

	private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		UpdateViewModelSelection();
	}

	private void ListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
	{
		TrackRealizedRow(args.ItemIndex, args.InRecycleQueue);
	}

	private async void SelectionHost_ItemInvoked(object? sender, TableViewItemInvokedEventArgs e)
	{
		if (e.Item is BrowseItemViewModel item)
		{
			await _viewModel.CommandManager.ExecuteAsync(CommandIds.OpenItem, item);
		}
	}

	private void SelectionHost_SelectionChanged(object? sender, EventArgs e)
	{
		UpdateViewModelSelection();
	}

	private void RowsHost_RowChanging(object? sender, TableViewRowChangingEventArgs e)
	{
		TrackRealizedRow(e.Index, e.InRecycleQueue);
	}

	private void UpdateViewModelSelection()
	{
		if (!_synchronizingSelection && !_viewModel.IsApplyingUpdate)
		{
			_viewModel.SetSelection(_selectedItems.OfType<BrowseItemViewModel>());
		}
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
