// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Files.Core.AppModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class PaneHost : UserControl
{
	private TabViewModel? subscribedViewModel;

	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(TabViewModel), typeof(PaneHost), new PropertyMetadata(null, ViewModelChanged));

	public PaneHost()
	{
		InitializeComponent();
		Loaded += PaneHost_Loaded;
		Unloaded += PaneHost_Unloaded;
		SizeChanged += PaneHost_SizeChanged;
	}

	public TabViewModel? ViewModel
	{
		get => (TabViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not PaneHost paneHost)
		{
			return;
		}

		paneHost.SetSubscribedViewModel(paneHost.IsLoaded ? args.NewValue as TabViewModel : null);
		paneHost.UpdateLayoutOrientation();
	}

	private void PaneHost_Loaded(object sender, RoutedEventArgs e)
	{
		SetSubscribedViewModel(ViewModel);
		UpdateLayoutOrientation();
	}

	private void PaneHost_Unloaded(object sender, RoutedEventArgs e) =>
		SetSubscribedViewModel(null);

	private void SetSubscribedViewModel(TabViewModel? value)
	{
		if (ReferenceEquals(subscribedViewModel, value))
		{
			return;
		}

		if (subscribedViewModel is not null)
		{
			subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
		}

		subscribedViewModel = value;
		if (subscribedViewModel is not null)
		{
			subscribedViewModel.PropertyChanged += ViewModel_PropertyChanged;
		}
	}

	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(TabViewModel.SplitOrientation))
		{
			UpdateLayoutOrientation();
		}
		else if (e.PropertyName is nameof(TabViewModel.ActivePane))
		{
			UpdatePaneShadows();
		}
	}

	private void UpdateLayoutOrientation()
	{
		if (ViewModel is { } viewModel)
		{
			PaneLayout.Orientation =
				viewModel.SplitOrientation is PaneSplitOrientation.Vertical
					? Orientation.Horizontal
					: Orientation.Vertical;
			UpdatePaneSizes();
			UpdatePaneShadows();
		}
	}

	private void PaneHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePaneSizes();

	private void PaneRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
	{
		UpdatePaneSizes();
		UpdatePaneShadows();
	}

	private void UpdatePaneSizes()
	{
		if (ViewModel is not { } viewModel || viewModel.Panes.Count is 0)
		{
			return;
		}

		var isSideBySide = viewModel.SplitOrientation is
			PaneSplitOrientation.Vertical;
		var spacing = viewModel.Panes.Count - 1;
		var paneWidth = isSideBySide
			? Math.Max(0, (ActualWidth - spacing) / viewModel.Panes.Count)
			: ActualWidth;
		var paneHeight = isSideBySide
			? ActualHeight
			: Math.Max(0, (ActualHeight - spacing) / viewModel.Panes.Count);

		for (var index = 0; index < viewModel.Panes.Count; index++)
		{
			if (PaneRepeater.TryGetElement(index) is FrameworkElement pane)
			{
				pane.Width = paneWidth;
				pane.Height = paneHeight;
			}
		}
	}

	private void UpdatePaneShadows()
	{
		if (ViewModel is not { } viewModel)
		{
			return;
		}

		var activePane = viewModel.ActivePane;
		var isMultiPane = viewModel.Panes.Count > 1;
		for (var index = 0; index < viewModel.Panes.Count; index++)
		{
			if (PaneRepeater.TryGetElement(index) is PaneView pane)
			{
				pane.SetShadow(ReferenceEquals(pane.ViewModel, activePane), isMultiPane);
			}
		}
	}

	private void PaneView_Activated(object sender, EventArgs e)
	{
		if (ViewModel is { } viewModel
			&& sender is PaneView { ViewModel: { } pane })
		{
			viewModel.SetActivePane(pane.Id);
		}
	}
}
