// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Commands;
using Files.Infrastructure;
using Files.ViewModels;
using Files.Core.Browsing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Files.Views;

internal sealed class FolderViewInteraction : IDisposable
{
	private readonly ListViewBase _listView;
	private readonly FolderBrowserViewModel _viewModel;
	private readonly HashSet<int> _realizedIndices = [];
	private readonly HashSet<IDetailsRowContent> _realizedDetailsRows = [];
	private int _containerContentChangeCount;
	private int _detailsRowBindingCount;
	private int _detailsRowRealizationCount;
	private int _viewportUpdateCount;
	private bool _firstContainerLogged;
	private bool _firstViewportLogged;
	private bool _synchronizingSelection;
	private bool _meaningfulRowDisplayPending;
	private bool _meaningfulRowDisplayed;
	private bool _viewportUpdateQueued;
	private bool _isDisposed;

	public FolderViewInteraction(ListViewBase listView, FolderBrowserViewModel viewModel)
	{
		_listView = listView;
		_viewModel = viewModel;
		UiDiagnosticLog.Write("FolderViewInteraction", $"created control={listView.GetType().Name} items={viewModel.Items.Count}");

		listView.DoubleTapped += ListView_DoubleTapped;
		listView.SelectionChanged += ListView_SelectionChanged;
		listView.ContainerContentChanging += ListView_ContainerContentChanging;
		listView.LayoutUpdated += ListView_LayoutUpdated;
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
		_listView.DoubleTapped -= ListView_DoubleTapped;
		_listView.SelectionChanged -= ListView_SelectionChanged;
		_listView.ContainerContentChanging -= ListView_ContainerContentChanging;
		_listView.LayoutUpdated -= ListView_LayoutUpdated;
		_viewModel.PropertyChanged -= ViewModel_PropertyChanged;
		_realizedIndices.Clear();
		_realizedDetailsRows.Clear();
		UiDiagnosticLog.Write(
			"FolderViewInteraction",
			$"disposed containers={_containerContentChangeCount} rowBindings={_detailsRowBindingCount} " +
			$"rowTemplates={_detailsRowRealizationCount} viewportUpdates={_viewportUpdateCount}");
	}

	private async void ListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
	{
		if (_listView.SelectedItem is not BrowseItemViewModel item)
		{
			return;
		}

		await _viewModel.CommandManager.ExecuteAsync(CommandIds.OpenItem, item);
	}

	private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_synchronizingSelection && !_viewModel.IsApplyingUpdate)
		{
			_viewModel.SetSelection(_listView.SelectedItems.OfType<BrowseItemViewModel>());
		}
	}

	private void ListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
	{
		var eventCount = Interlocked.Increment(ref _containerContentChangeCount);
		if (eventCount <= 10 || eventCount % 100 is 0)
		{
			UiDiagnosticLog.Write("FolderViewInteraction", $"ContainerContentChanging count={eventCount} index={args.ItemIndex} recycled={args.InRecycleQueue} realizedBefore={_realizedIndices.Count}");
		}
		if (!_firstContainerLogged && !args.InRecycleQueue)
		{
			_firstContainerLogged = true;
			UiDiagnosticLog.Write("FolderViewInteraction", $"First container realized index={args.ItemIndex}");
		}

		if (args.InRecycleQueue)
		{
			_realizedIndices.Remove(args.ItemIndex);
			if (args.ItemContainer.ContentTemplateRoot is IDetailsRowContent recycledRow)
			{
				_realizedDetailsRows.Remove(recycledRow);
			}
		}
		else
		{
			_realizedIndices.Add(args.ItemIndex);
			if (DetailsRowRealization.TryBind(args.ItemContainer.ContentTemplateRoot, _viewModel.DetailsColumns, out var row))
			{
				if (_realizedDetailsRows.Add(row!))
				{
					Interlocked.Increment(ref _detailsRowRealizationCount);
				}
				if (row!.HasMeaningfulContent)
				{
					var bindingCount = Interlocked.Increment(ref _detailsRowBindingCount);
					if (bindingCount is 1)
					{
						UiDiagnosticLog.Write("FolderViewInteraction", $"First meaningful row bound index={args.ItemIndex} items={_viewModel.Items.Count}");
						_meaningfulRowDisplayPending = true;
					}
				}
			}
		}

		QueueViewportUpdate();
	}

	private void ListView_LayoutUpdated(object? sender, object args)
	{
		if (!_meaningfulRowDisplayPending || _meaningfulRowDisplayed)
		{
			return;
		}

		_meaningfulRowDisplayPending = false;
		_meaningfulRowDisplayed = true;
		UiDiagnosticLog.Write("FolderViewInteraction", "First meaningful row displayed after layout");
	}

	private void QueueViewportUpdate()
	{
		if (_viewportUpdateQueued || _isDisposed)
		{
			return;
		}

		_viewportUpdateQueued = true;
		if (!_listView.DispatcherQueue.TryEnqueue(UpdateViewport))
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
		var scale = _listView.XamlRoot?.RasterizationScale ?? 1.0;

		return Math.Max(1, (int)Math.Round(scale * 96.0));
	}

	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(FolderBrowserViewModel.SelectedKeys))
		{
			SynchronizeSelection();
		}
		else if (e.PropertyName is nameof(FolderBrowserViewModel.DetailsColumns))
		{
			foreach (var row in _realizedDetailsRows)
			{
				row.Columns = _viewModel.DetailsColumns;
			}
		}
	}

	private void SynchronizeSelection()
	{
		_synchronizingSelection = true;
		try
		{
			var selectedKeys = _viewModel.SelectedKeys.ToHashSet();
			_listView.SelectedItems.Clear();
			foreach (var item in _viewModel.Items)
			{
				if (selectedKeys.Contains(item.Reference.GetKey()))
				{
					_listView.SelectedItems.Add(item);
				}
			}
		}
		finally
		{
			_synchronizingSelection = false;
		}
	}
}
