// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Specialized;
using Files.Controls;
using Files.ViewModels;
using Files.Core.Sessions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class PaneHost : UserControl
{
	private const double MinimumPaneSize = 100;
	private const double SplitterSize = 4;

	private readonly Dictionary<Guid, PaneView> _paneViews = [];
	private TabViewModel? _subscribedViewModel;

	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(TabViewModel), typeof(PaneHost), new PropertyMetadata(null, ViewModelChanged));

	public TabViewModel? ViewModel
	{
		get => (TabViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public PaneHost()
	{
		InitializeComponent();
		Loaded += PaneHost_Loaded;
		Unloaded += PaneHost_Unloaded;
	}

	private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not PaneHost paneHost)
		{
			return;
		}

		if (!paneHost.IsLoaded)
		{
			return;
		}

		paneHost.SetSubscribedViewModel(args.NewValue as TabViewModel);
		paneHost.UpdatePaneLayout();
	}

	private void PaneHost_Loaded(object sender, RoutedEventArgs e)
	{
		SetSubscribedViewModel(ViewModel);
		UpdatePaneLayout();
	}

	private void PaneHost_Unloaded(object sender, RoutedEventArgs e) =>
		SetSubscribedViewModel(null);

	private void SetSubscribedViewModel(TabViewModel? value)
	{
		if (ReferenceEquals(_subscribedViewModel, value))
		{
			return;
		}

		if (_subscribedViewModel is not null)
		{
			_subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
			_subscribedViewModel.Panes.CollectionChanged -= Panes_CollectionChanged;
		}

		_subscribedViewModel = value;
		if (_subscribedViewModel is not null)
		{
			_subscribedViewModel.PropertyChanged += ViewModel_PropertyChanged;
			_subscribedViewModel.Panes.CollectionChanged += Panes_CollectionChanged;
		}
	}

	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(TabViewModel.SplitOrientation))
		{
			UpdatePaneLayout();
		}
		else if (e.PropertyName is nameof(TabViewModel.ActivePane))
		{
			UpdatePaneShadows();
		}
	}

	private void Panes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdatePaneLayout();

	private void UpdatePaneLayout()
	{
		if (ViewModel is not { } viewModel)
		{
			ClearPaneViews();
			PaneGrid.Children.Clear();
			PaneGrid.ColumnDefinitions.Clear();
			PaneGrid.RowDefinitions.Clear();

			return;
		}

		var currentPaneIds = viewModel.Panes.Select(static pane => pane.Id).ToHashSet();
		foreach (var removedPaneId in _paneViews.Keys.Where(paneId => !currentPaneIds.Contains(paneId)).ToArray())
		{
			_paneViews[removedPaneId].Activated -= PaneView_Activated;
			_paneViews.Remove(removedPaneId);
		}

		PaneGrid.Children.Clear();
		PaneGrid.ColumnDefinitions.Clear();
		PaneGrid.RowDefinitions.Clear();

		var isSideBySide = viewModel.SplitOrientation is PaneSplitOrientation.Vertical;
		for (var index = 0; index < viewModel.Panes.Count; index++)
		{
			if (index > 0)
			{
				AddSplitter(isSideBySide, (index * 2) - 1);
			}

			AddPaneDefinition(isSideBySide);
			var paneViewModel = viewModel.Panes[index];
			if (!_paneViews.TryGetValue(paneViewModel.Id, out var paneView) || !ReferenceEquals(paneView.ViewModel, paneViewModel))
			{
				if (paneView is not null)
				{
					paneView.Activated -= PaneView_Activated;
				}

				paneView = new PaneView
				{
					HorizontalAlignment = HorizontalAlignment.Stretch,
					VerticalAlignment = VerticalAlignment.Stretch,
					ViewModel = paneViewModel,
				};
				paneView.Activated += PaneView_Activated;
				_paneViews[paneViewModel.Id] = paneView;
			}

			var gridIndex = index * 2;
			Grid.SetColumn(paneView, isSideBySide ? gridIndex : 0);
			Grid.SetRow(paneView, isSideBySide ? 0 : gridIndex);
			PaneGrid.Children.Add(paneView);
		}

		UpdatePaneShadows();
	}

	private void UpdatePaneShadows()
	{
		if (ViewModel is not { } viewModel)
		{
			return;
		}

		var activePane = viewModel.ActivePane;
		var isMultiPane = viewModel.Panes.Count > 1;
		foreach (var pane in _paneViews.Values)
		{
			pane.SetShadow(ReferenceEquals(pane.ViewModel, activePane), isMultiPane);
		}
	}

	private void PaneView_Activated(object? sender, EventArgs e)
	{
		if (ViewModel is { } viewModel && sender is PaneView { ViewModel: { } pane })
		{
			viewModel.SetActivePane(pane.Id);
		}
	}

	private void AddPaneDefinition(bool isSideBySide)
	{
		if (isSideBySide)
		{
			PaneGrid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = MinimumPaneSize, Width = new GridLength(1, GridUnitType.Star) });
		}
		else
		{
			PaneGrid.RowDefinitions.Add(new RowDefinition { MinHeight = MinimumPaneSize, Height = new GridLength(1, GridUnitType.Star) });
		}
	}

	private void AddSplitter(bool isSideBySide, int gridIndex)
	{
		var splitter = new GridSplitter
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			IsTabStop = false,
			MinHeight = 0,
			MinWidth = 0,
			Opacity = 0,
			ResizeBehavior = GridResizeBehavior.PreviousAndNext,
			ResizeDirection = isSideBySide ? GridResizeDirection.Columns : GridResizeDirection.Rows,
		};

		if (isSideBySide)
		{
			PaneGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SplitterSize) });
			Grid.SetColumn(splitter, gridIndex);
		}
		else
		{
			PaneGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SplitterSize) });
			Grid.SetRow(splitter, gridIndex);
		}

		PaneGrid.Children.Add(splitter);
	}

	private void ClearPaneViews()
	{
		foreach (var pane in _paneViews.Values)
		{
			pane.Activated -= PaneView_Activated;
		}

		_paneViews.Clear();
	}
}
