// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Adapters;
using Files.Controls;
using Files.Core.Browsing;
using Files.Infrastructure;
using Files.Localization;
using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Files.Views;

public sealed partial class NavigationToolbar : UserControl
{
	private const double BreadcrumbIconSize = 16;
	private const string FolderIconGlyph = "\uE8B7";

	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(NavigationToolbarViewModel), typeof(NavigationToolbar), new PropertyMetadata(null));

	private CancellationTokenSource? _breadcrumbFlyoutCancellation;

	public NavigationToolbarViewModel? ViewModel
	{
		get => (NavigationToolbarViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public NavigationToolbar()
	{
		InitializeComponent();
		Unloaded += NavigationToolbar_Unloaded;
	}

	private async void PathOmnibar_QuerySubmitted(Omnibar sender, OmnibarQuerySubmittedEventArgs args)
	{
		await NavigatePathAsync(args.Text);
	}

	private async void PathBreadcrumbBar_ItemClicked(Files.Controls.BreadcrumbBar sender, Files.Controls.BreadcrumbBarItemClickedEventArgs args)
	{
		if (ViewModel is not { } viewModel)
		{
			return;
		}

		if (args.IsRootItem)
		{
			await viewModel.NavigateHomeAsync();

			return;
		}

		if (args.Index < 0 || args.Index >= viewModel.BreadcrumbItems.Count)
		{
			return;
		}

		await viewModel.NavigateToBreadcrumbAsync(viewModel.BreadcrumbItems[args.Index]);
	}

	private async void PathBreadcrumbBar_ItemDropDownFlyoutOpening(object sender, BreadcrumbBarItemDropDownFlyoutEventArgs args)
	{
		if (ViewModel is not { } viewModel)
		{
			return;
		}

		BrowseLocation? location = args.IsRootItem
			? HomeLocation.Instance
			: args.Index >= 0 && args.Index < viewModel.BreadcrumbItems.Count
				? viewModel.BreadcrumbItems[args.Index].Location
				: null;
		if (location is null)
		{
			args.Flyout.Items.Clear();

			return;
		}

		_breadcrumbFlyoutCancellation?.Cancel();
		var cancellation = new CancellationTokenSource();
		_breadcrumbFlyoutCancellation = cancellation;
		args.Flyout.Items.Clear();
		args.Flyout.Items.Add(new MenuFlyoutItem { IsEnabled = false, Text = Strings.Loading.GetLocalized() });
		try
		{
			var children = await viewModel.GetBreadcrumbChildrenAsync(location, cancellation.Token);
			if (cancellation.IsCancellationRequested || !ReferenceEquals(_breadcrumbFlyoutCancellation, cancellation))
			{
				return;
			}

			args.Flyout.Items.Clear();
			foreach (var child in children)
			{
				var menuItem = new MenuFlyoutItem { Icon = CreateBreadcrumbChildIcon(child), Tag = child, Text = child.Text };
				menuItem.Click += BreadcrumbChild_Click;
				args.Flyout.Items.Add(menuItem);
			}
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			UiDiagnosticLog.Write("NavigationToolbar", $"Breadcrumb child enumeration failed: {error.Message}");
			if (ReferenceEquals(_breadcrumbFlyoutCancellation, cancellation))
			{
				args.Flyout.Items.Clear();
			}
		}
		finally
		{
			if (ReferenceEquals(_breadcrumbFlyoutCancellation, cancellation))
			{
				_breadcrumbFlyoutCancellation = null;
			}

			cancellation.Dispose();
		}
	}

	private void PathBreadcrumbBar_ItemDropDownFlyoutClosed(object sender, BreadcrumbBarItemDropDownFlyoutEventArgs args)
	{
		_breadcrumbFlyoutCancellation?.Cancel();
		args.Flyout.Items.Clear();
	}

	private void PathBreadcrumbBar_Tapped(object sender, TappedRoutedEventArgs e)
	{
		var source = e.OriginalSource as DependencyObject;
		while (source is not null && !ReferenceEquals(source, PathBreadcrumbBar))
		{
			if (source is Button)
			{
				return;
			}

			source = VisualTreeHelper.GetParent(source);
		}

		PathOmnibar.FocusTextBox();
	}

	private async void BreadcrumbChild_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not MenuFlyoutItem { Tag: NavigationToolbarBreadcrumbItem item } || ViewModel is not { } viewModel)
		{
			return;
		}

		await viewModel.NavigateToBreadcrumbAsync(item);
	}

	private void NavigationToolbar_Unloaded(object sender, RoutedEventArgs e)
	{
		_breadcrumbFlyoutCancellation?.Cancel();
		_breadcrumbFlyoutCancellation = null;
	}

	private static IconElement CreateBreadcrumbChildIcon(NavigationToolbarBreadcrumbItem item)
	{
		if (item.ThumbnailData.IsEmpty)
		{
			return new FontIcon { Glyph = FolderIconGlyph };
		}

		try
		{
			return new ImageIcon { Height = BreadcrumbIconSize, Source = ThumbnailImageFactory.Create(item.ThumbnailData), Width = BreadcrumbIconSize };
		}
		catch (Exception error)
		{
			UiDiagnosticLog.Write("NavigationToolbar", $"Breadcrumb thumbnail decode failed: {error.GetType().Name}");

			return new FontIcon { Glyph = FolderIconGlyph };
		}
	}

	private async Task NavigatePathAsync(string path)
	{
		if (ViewModel is not { } viewModel || string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		await viewModel.NavigatePathCommand.ExecuteAsync(path);
	}
}
