// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;

namespace Files.Views;

public sealed partial class FolderBrowser : Microsoft.UI.Xaml.Controls.UserControl
{
	private FolderBrowserViewModel? subscribedViewModel;

	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(FolderBrowserViewModel), typeof(FolderBrowser), new PropertyMetadata(null, ViewModelChanged));

	public FolderBrowser()
	{
		InitializeComponent();
		Loaded += FolderBrowser_Loaded;
		Unloaded += FolderBrowser_Unloaded;
	}

	public FolderBrowserViewModel? ViewModel
	{
		get => (FolderBrowserViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not FolderBrowser folderBrowser)
		{
			return;
		}

		folderBrowser.SetSubscribedViewModel(folderBrowser.IsLoaded ? args.NewValue as FolderBrowserViewModel : null);
		folderBrowser.UpdateFolderView();
	}

	private void FolderBrowser_Loaded(object sender, RoutedEventArgs e) =>
		SetSubscribedViewModel(ViewModel);

	private void FolderBrowser_Unloaded(object sender, RoutedEventArgs e) =>
		SetSubscribedViewModel(null);

	private void SetSubscribedViewModel(FolderBrowserViewModel? value)
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
		if (e.PropertyName is nameof(FolderBrowserViewModel.ViewMode))
		{
			UpdateFolderView();
		}
	}

	private void UpdateFolderView()
	{
		if (ViewModel is not { } viewModel)
		{
			FolderViewPresenter.Content = null;
			FolderViewPresenter.ContentTemplate = null;
			return;
		}

		FolderViewPresenter.Content = viewModel;
		FolderViewPresenter.ContentTemplate = viewModel.ViewMode switch
		{
			FolderViewMode.Details =>
				(DataTemplate)FolderViewPresenter.Resources["DetailsTemplate"],
			FolderViewMode.Grid =>
				(DataTemplate)FolderViewPresenter.Resources["GridTemplate"],
			FolderViewMode.List =>
				(DataTemplate)FolderViewPresenter.Resources["ListTemplate"],
			_ => throw new InvalidOperationException($"Unsupported folder view mode: {viewModel.ViewMode}."),
		};
	}
}
