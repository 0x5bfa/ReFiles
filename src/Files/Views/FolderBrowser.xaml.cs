// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;

namespace Files.Views;

public sealed partial class FolderBrowser : Microsoft.UI.Xaml.Controls.UserControl
{
	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(FolderBrowserViewModel), typeof(FolderBrowser), new PropertyMetadata(null, ViewModelChanged));

	private FolderBrowserViewModel? _subscribedViewModel;

	public FolderBrowserViewModel? ViewModel
	{
		get => (FolderBrowserViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public FolderBrowser()
	{
		InitializeComponent();
		Loaded += FolderBrowser_Loaded;
		Unloaded += FolderBrowser_Unloaded;
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
		if (ReferenceEquals(_subscribedViewModel, value))
		{
			return;
		}

		if (_subscribedViewModel is not null)
		{
			_subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
		}

		_subscribedViewModel = value;
		if (_subscribedViewModel is not null)
		{
			_subscribedViewModel.PropertyChanged += ViewModel_PropertyChanged;
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
			FolderViewMode.Cards =>
				(DataTemplate)FolderViewPresenter.Resources["CardsTemplate"],
			FolderViewMode.Grid =>
				(DataTemplate)FolderViewPresenter.Resources["GridTemplate"],
			FolderViewMode.List =>
				(DataTemplate)FolderViewPresenter.Resources["ListTemplate"],
			FolderViewMode.Columns =>
				(DataTemplate)FolderViewPresenter.Resources["ColumnsTemplate"],
			_ => throw new InvalidOperationException($"Unsupported folder view mode: {viewModel.ViewMode}."),
		};
	}
}
