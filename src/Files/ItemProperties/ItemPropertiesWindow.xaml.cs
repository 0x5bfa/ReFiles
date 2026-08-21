// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Files.Core.Storage.Windows;
using Files.Localization;
using Files.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Files.ItemProperties;

public sealed partial class ItemPropertiesWindow : Window
{
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Func<CancellationToken, Task<IReadOnlyList<WindowsShellPropertyPage>>>? _getPropertyPages;
	private bool _isInitialized;

	internal ItemPropertiesViewModel ViewModel { get; }

	internal ItemPropertiesWindow(IReadOnlyList<BrowseItemViewModel> items, Func<CancellationToken, Task<IReadOnlyList<WindowsShellPropertyPage>>>? getPropertyPages = null)
	{
		ViewModel = new(items);
		_getPropertyPages = getPropertyPages;
		InitializeComponent();
		Title = ViewModel.WindowTitle;
		AppWindow.Resize(new SizeInt32(760, 700));
		Activated += Window_Activated;
		Closed += Window_Closed;
	}

	internal Visibility ToVisibility(bool value)
	{
		return value ? Visibility.Visible : Visibility.Collapsed;
	}

	internal Visibility ToInverseVisibility(bool value)
	{
		return value ? Visibility.Collapsed : Visibility.Visible;
	}

	private async void Window_Activated(object sender, WindowActivatedEventArgs args)
	{
		if (_isInitialized)
		{
			return;
		}

		_isInitialized = true;
		try
		{
			await Task.WhenAll(ViewModel.InitializeAsync(_lifetime.Token), PopulatePropertyTabsAsync(_lifetime.Token));
		}
		catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			ShowError(exception.Message);
		}
	}

	private async void OkButton_Click(object sender, RoutedEventArgs e)
	{
		if (await TryApplyAsync())
		{
			Close();
		}
	}

	private async void ApplyButton_Click(object sender, RoutedEventArgs e)
	{
		await TryApplyAsync();
	}

	private void CancelButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private async Task<bool> TryApplyAsync()
	{
		ErrorInfoBar.IsOpen = false;
		try
		{
			await ViewModel.ApplyAsync(_lifetime.Token);
			Title = ViewModel.WindowTitle;

			return true;
		}
		catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
		{
			return false;
		}
		catch (Exception exception)
		{
			ShowError(exception.Message);

			return false;
		}
	}

	private async Task PopulatePropertyTabsAsync(CancellationToken cancellationToken)
	{
		if (_getPropertyPages is null)
		{
			return;
		}

		var pages = await _getPropertyPages(cancellationToken);
		if (pages.Count is 0)
		{
			return;
		}

		PropertyTabs.TabItems.Clear();
		var usedGeneralPage = false;
		var usedDetailsPage = false;
		for (var index = 0; index < pages.Count; index++)
		{
			var page = pages[index];
			var title = string.IsNullOrWhiteSpace(page.Title)
				? string.Format(CultureInfo.CurrentCulture, Strings.PropertyPageFallbackFormat.GetLocalized(), index + 1)
				: page.Title;
			if (page.IsDefault && !usedGeneralPage)
			{
				GeneralTab.Header = title;
				PropertyTabs.TabItems.Add(GeneralTab);
				usedGeneralPage = true;
			}
			else if (!usedDetailsPage && title.Equals(ViewModel.DetailsLabel, StringComparison.CurrentCultureIgnoreCase))
			{
				DetailsTab.Header = title;
				PropertyTabs.TabItems.Add(DetailsTab);
				usedDetailsPage = true;
			}
			else
			{
				PropertyTabs.TabItems.Add(CreateUnavailablePage(title));
			}
		}
	}

	private static TabViewItem CreateUnavailablePage(string title)
	{
		var content = new StackPanel
		{
			Padding = new Thickness(24),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 16,
		};
		content.Children.Add(new FontIcon { FontSize = 40, Glyph = "\uE713" });
		content.Children.Add(new TextBlock
		{
			MaxWidth = 480,
			HorizontalAlignment = HorizontalAlignment.Center,
			Text = string.Format(CultureInfo.CurrentCulture, Strings.PropertyPageNotImplementedFormat.GetLocalized(), title),
			TextAlignment = TextAlignment.Center,
			TextWrapping = TextWrapping.Wrap,
		});

		return new TabViewItem { Header = title, IsClosable = false, Content = content };
	}

	private void Window_Closed(object sender, WindowEventArgs args)
	{
		Activated -= Window_Activated;
		Closed -= Window_Closed;
		_lifetime.Cancel();
		_lifetime.Dispose();
	}

	private void ShowError(string message)
	{
		ErrorInfoBar.Message = message;
		ErrorInfoBar.IsOpen = true;
	}
}
