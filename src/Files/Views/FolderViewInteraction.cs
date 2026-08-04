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
	private readonly ListViewBase listView;
	private readonly FolderBrowserViewModel viewModel;
	private readonly HashSet<int> realizedIndices = [];
	private bool synchronizingSelection;
	private bool viewportUpdateQueued;
	private bool isDisposed;

	public FolderViewInteraction(ListViewBase listView, FolderBrowserViewModel viewModel)
	{
		this.listView = listView;
		this.viewModel = viewModel;

		listView.DoubleTapped += ListView_DoubleTapped;
		listView.SelectionChanged += ListView_SelectionChanged;
		listView.ContainerContentChanging += ListView_ContainerContentChanging;
		viewModel.PropertyChanged += ViewModel_PropertyChanged;
		SynchronizeSelection();
	}

	public void Dispose()
	{
		if (isDisposed)
		{
			return;
		}

		isDisposed = true;
		listView.DoubleTapped -= ListView_DoubleTapped;
		listView.SelectionChanged -= ListView_SelectionChanged;
		listView.ContainerContentChanging -= ListView_ContainerContentChanging;
		viewModel.PropertyChanged -= ViewModel_PropertyChanged;
		realizedIndices.Clear();
	}

	private async void ListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
	{
		if (listView.SelectedItem is not BrowseItemViewModel item)
		{
			return;
		}

		await viewModel.CommandManager.ExecuteAsync(CommandIds.OpenItem, item);
	}

	private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!synchronizingSelection && !viewModel.IsApplyingUpdate)
		{
			viewModel.SetSelection(listView.SelectedItems.OfType<BrowseItemViewModel>());
		}
	}

	private void ListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
	{
		if (args.InRecycleQueue)
		{
			realizedIndices.Remove(args.ItemIndex);
		}
		else
		{
			realizedIndices.Add(args.ItemIndex);
		}

		QueueViewportUpdate();
	}

	private void QueueViewportUpdate()
	{
		if (viewportUpdateQueued || isDisposed)
		{
			return;
		}

		viewportUpdateQueued = true;
		if (!listView.DispatcherQueue.TryEnqueue(UpdateViewport))
		{
			viewportUpdateQueued = false;
		}
	}

	private void UpdateViewport()
	{
		viewportUpdateQueued = false;
		if (isDisposed)
		{
			return;
		}

		if (realizedIndices.Count is 0)
		{
			viewModel.UpdateViewport(new BrowseViewport(0, 0, dpi: GetDpi()));

			return;
		}

		var firstIndex = realizedIndices.Min();
		var lastIndex = realizedIndices.Max();
		viewModel.UpdateViewport(new BrowseViewport(firstIndex, lastIndex - firstIndex + 1, dpi: GetDpi()));
	}

	private int GetDpi()
	{
		var scale = listView.XamlRoot?.RasterizationScale ?? 1.0;

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
		synchronizingSelection = true;
		try
		{
			var selectedKeys = viewModel.SelectedKeys;
			listView.SelectedItems.Clear();
			foreach (var item in viewModel.Items)
			{
				if (selectedKeys.Contains(item.Reference.GetKey()))
				{
					listView.SelectedItems.Add(item);
				}
			}
		}
		finally
		{
			synchronizingSelection = false;
		}
	}
}
