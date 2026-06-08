// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class PaneContentView : UserControl
{
	private PaneViewModel? subscribedViewModel;

	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(
			nameof(ViewModel),
			typeof(PaneViewModel),
			typeof(PaneContentView),
			new PropertyMetadata(null, ViewModelChanged));

	public PaneContentView()
	{
		InitializeComponent();
		Loaded += PaneContentView_Loaded;
		Unloaded += PaneContentView_Unloaded;
	}

	public PaneViewModel? ViewModel
	{
		get => (PaneViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	private static void ViewModelChanged(
		DependencyObject sender,
		DependencyPropertyChangedEventArgs args)
	{
		if (sender is not PaneContentView view)
		{
			return;
		}

		view.SetSubscribedViewModel(
			view.IsLoaded ? args.NewValue as PaneViewModel : null);
		view.UpdateContent();
	}

	private void PaneContentView_Loaded(object sender, RoutedEventArgs e) =>
		SetSubscribedViewModel(ViewModel);

	private void PaneContentView_Unloaded(object sender, RoutedEventArgs e) =>
		SetSubscribedViewModel(null);

	private void SetSubscribedViewModel(PaneViewModel? value)
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

	private void ViewModel_PropertyChanged(
		object? sender,
		PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(PaneViewModel.ContentKind))
		{
			UpdateContent();
		}
	}

	private void UpdateContent()
	{
		if (ViewModel is not { } viewModel)
		{
			PaneContentPresenter.Content = null;
			PaneContentPresenter.ContentTemplate = null;
			return;
		}

		PaneContentPresenter.Content = viewModel;
		PaneContentPresenter.ContentTemplate = viewModel.ContentKind switch
		{
			PaneContentKind.FolderBrowser =>
				(DataTemplate)PaneContentPresenter.Resources["FolderBrowserTemplate"],
			PaneContentKind.Settings =>
				(DataTemplate)PaneContentPresenter.Resources["SettingsTemplate"],
			PaneContentKind.Web =>
				(DataTemplate)PaneContentPresenter.Resources["WebTemplate"],
			_ => throw new InvalidOperationException(
				$"Unsupported pane content kind: {viewModel.ContentKind}."),
		};
	}
}
