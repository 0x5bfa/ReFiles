// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.Globalization;
using Files.Adapters;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Storage.Windows;
using Files.Localization;
using Files.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Win32.Foundation;

namespace Files.ItemProperties;

public sealed partial class ItemPropertiesWindow : Window
{
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Func<CancellationToken, Task<IReadOnlyList<WindowsShellPropertyPage>>>? _getPropertyPages;
	private readonly Func<WindowsShellPropertyPageKind, CancellationToken, Task<WindowsShellPropertySheetData?>>? _getPropertyPageData;
	private readonly Func<CancellationToken, Task<(string? Description, ThumbnailResult? Icon)>>? _getGeneralProperties;
	private readonly Dictionary<WindowsShellPropertyPageKind, UserControl> _propertyViewCache = [];
	private readonly HashSet<WindowsShellPropertyPageKind> _loadedPropertyPages = [WindowsShellPropertyPageKind.General];
	private readonly HashSet<WindowsShellPropertyPageKind> _loadingPropertyPages = [];
	private readonly List<WindowsShellPropertyPageKind> _propertyPageKinds = [];
	private readonly List<UserControl> _propertyViews = [];
	private readonly GeneralPropertyView _generalView;
	private readonly DetailsPropertyView _detailsView;
	private bool _isInitialized;

	internal ItemPropertiesViewModel ViewModel { get; }

	internal ItemPropertiesWindow(
		IReadOnlyList<BrowseItemViewModel> items,
		Func<CancellationToken, Task<IReadOnlyList<WindowsShellPropertyPage>>>? getPropertyPages = null,
		Func<WindowsShellPropertyPageKind, CancellationToken, Task<WindowsShellPropertySheetData?>>? getPropertyPageData = null,
		Func<CancellationToken, Task<(string? Description, ThumbnailResult? Icon)>>? getGeneralProperties = null)
	{
		ViewModel = new(items);
		_getPropertyPages = getPropertyPages;
		_getPropertyPageData = getPropertyPageData;
		_getGeneralProperties = getGeneralProperties;
		InitializeComponent();
		_generalView = new(ViewModel, ShowError);
		_detailsView = new(ViewModel);
		_propertyViewCache[WindowsShellPropertyPageKind.General] = _generalView;
		_propertyViewCache[WindowsShellPropertyPageKind.Details] = _detailsView;
		RegisterPropertyView(Strings.General.GetLocalized(), WindowsShellPropertyPageKind.General, _generalView);
		Title = ViewModel.WindowTitle;
		AppWindow.Resize(new SizeInt32(540, 650));
		Activated += Window_Activated;
		Closed += Window_Closed;
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
			await Task.WhenAll(ViewModel.InitializeAsync(_lifetime.Token), PopulatePropertyViewsAsync(_lifetime.Token), PopulateGeneralPropertiesAsync(_lifetime.Token));
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
			if (ViewModel.RequiresAttributeScopeSelection)
			{
				var scopeDialog = new AttributeScopeDialog(ViewModel) { XamlRoot = Content.XamlRoot };
				if (await scopeDialog.ShowAsync() is not ContentDialogResult.Primary)
				{
					return false;
				}

				ViewModel.ApplyToContents = scopeDialog.ApplyToContents;
			}

			var owner = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this));
			await ViewModel.ApplyAsync(owner, _lifetime.Token);
			if (_propertyViewCache.TryGetValue(WindowsShellPropertyPageKind.Customize, out var customizeView) && customizeView is CustomizePropertyView customizePropertyView)
			{
				customizePropertyView.Apply();
			}

			Title = ViewModel.WindowTitle;

			return true;
		}
		catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
		{
			return false;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (Exception exception)
		{
			ShowError(exception.Message);

			return false;
		}
	}

	private async Task PopulatePropertyViewsAsync(CancellationToken cancellationToken)
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

		PropertyPageSelector.Items.Clear();
		_propertyPageKinds.Clear();
		_propertyViews.Clear();
		for (var childIndex = PropertyPagePanel.Children.Count - 1; childIndex >= 0; childIndex--)
		{
			if (PropertyPagePanel.Children[childIndex] != _generalView && PropertyPagePanel.Children[childIndex] != _detailsView)
			{
				PropertyPagePanel.Children.RemoveAt(childIndex);
			}
		}

		for (var index = 0; index < pages.Count; index++)
		{
			var page = pages[index];
			var title = GetPageTitle(page, index);
			if (!_propertyViewCache.TryGetValue(page.Kind, out var view))
			{
				view = new MessagePropertyView(Strings.Loading.GetLocalized());
				view.Tag = title;
			}

			RegisterPropertyView(title, page.Kind, view);
		}

		LoadSelectedPropertyPage();
	}

	private async Task PopulateGeneralPropertiesAsync(CancellationToken cancellationToken)
	{
		if (_getGeneralProperties is null)
		{
			return;
		}

		var properties = await _getGeneralProperties(cancellationToken);
		var icon = properties.Icon is null ? null : await ThumbnailImageFactory.CreateAsync(properties.Icon.Content);
		ViewModel.SetGeneralShellProperties(properties.Description, icon);
	}

	private void RegisterPropertyView(string title, WindowsShellPropertyPageKind kind, UserControl view)
	{
		var isFirstView = _propertyViews.Count is 0;
		view.Visibility = isFirstView ? Visibility.Visible : Visibility.Collapsed;
		if (!PropertyPagePanel.Children.Contains(view))
		{
			PropertyPagePanel.Children.Add(view);
		}

		_propertyPageKinds.Add(kind);
		_propertyViews.Add(view);
		PropertyPageSelector.Items.Add(new SelectorBarItem { IsSelected = isFirstView, Text = title });
	}

	private void PropertyPageSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
	{
		var selectedIndex = sender.SelectedItem is null ? -1 : sender.Items.IndexOf(sender.SelectedItem);
		for (var index = 0; index < _propertyViews.Count; index++)
		{
			_propertyViews[index].Visibility = index == selectedIndex ? Visibility.Visible : Visibility.Collapsed;
		}

		LoadSelectedPropertyPage();
	}

	private void LoadSelectedPropertyPage()
	{
		var selectedIndex = PropertyPageSelector.SelectedItem is null ? -1 : PropertyPageSelector.Items.IndexOf(PropertyPageSelector.SelectedItem);
		if (selectedIndex >= 0 && selectedIndex < _propertyPageKinds.Count)
		{
			_ = LoadPropertyPageAsync(_propertyPageKinds[selectedIndex], _lifetime.Token);
		}
	}

	private async Task LoadPropertyPageAsync(WindowsShellPropertyPageKind kind, CancellationToken cancellationToken)
	{
		if (_getPropertyPageData is null || _loadedPropertyPages.Contains(kind) || !_loadingPropertyPages.Add(kind))
		{
			return;
		}

		try
		{
			var data = await _getPropertyPageData(kind, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			if (data is null)
			{
				return;
			}

			if (kind is WindowsShellPropertyPageKind.Details)
			{
				ViewModel.SetShellDetails(data.Details);
			}

			var view = CreatePropertyView(kind, data);
			ReplacePropertyView(kind, view);
			_loadedPropertyPages.Add(kind);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			ReplacePropertyView(kind, new MessagePropertyView(exception.Message));
			ShowError(exception.Message);
		}
		finally
		{
			_loadingPropertyPages.Remove(kind);
		}
	}

	private void ReplacePropertyView(WindowsShellPropertyPageKind kind, UserControl view)
	{
		var index = _propertyPageKinds.IndexOf(kind);
		if (index < 0)
		{
			return;
		}

		var oldView = _propertyViews[index];
		var childIndex = PropertyPagePanel.Children.IndexOf(oldView);
		if (childIndex < 0)
		{
			return;
		}

		view.Tag = oldView.Tag;
		view.Visibility = oldView.Visibility;
		PropertyPagePanel.Children.RemoveAt(childIndex);
		PropertyPagePanel.Children.Insert(childIndex, view);
		_propertyViews[index] = view;
		_propertyViewCache[kind] = view;
	}

	private UserControl CreatePropertyView(WindowsShellPropertyPageKind kind, WindowsShellPropertySheetData data)
	{
		return kind switch
		{
			WindowsShellPropertyPageKind.General => _generalView,
			WindowsShellPropertyPageKind.Tools when data.Drive is not null => new ToolsPropertyView(data.Drive, new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this)), ShowError),
			WindowsShellPropertyPageKind.Hardware => new HardwarePropertyView(data.HardwareDevices, new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this)), ShowError),
			WindowsShellPropertyPageKind.Shortcut when data.Shortcut is not null => new ShortcutPropertyView(data.Shortcut),
			WindowsShellPropertyPageKind.Compatibility when data.Compatibility is not null => new CompatibilityPropertyView(data.Compatibility, LaunchSystemTool),
			WindowsShellPropertyPageKind.Sharing when data.Sharing is not null => new SharingPropertyView(data.Sharing, new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this)), ShowError),
			WindowsShellPropertyPageKind.Security when data.Security is not null => new SecurityPropertyView(data.Security, new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this)), ShowError),
			WindowsShellPropertyPageKind.PreviousVersions => new PreviousVersionsPropertyView(data.PreviousVersions, LaunchSystemTool),
			WindowsShellPropertyPageKind.Quota when data.Quota is not null => new QuotaPropertyView(data.Quota, new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this)), ShowError),
			WindowsShellPropertyPageKind.Customize when data.Customization is not null => new CustomizePropertyView(
				data.Customization, ViewModel, new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this)), ShowError),
			WindowsShellPropertyPageKind.DigitalSignatures => new DigitalSignaturesPropertyView(data.EmbeddedSignatures, data.CatalogSignatures),
			WindowsShellPropertyPageKind.Details => _detailsView,
			_ => new MessagePropertyView(Strings.Unspecified.GetLocalized()),
		};
	}

	private static string GetPageTitle(WindowsShellPropertyPage page, int index)
	{
		if (!string.IsNullOrWhiteSpace(page.Title))
		{
			return page.Title;
		}

		return page.Kind switch
		{
			WindowsShellPropertyPageKind.General => Strings.General.GetLocalized(),
			WindowsShellPropertyPageKind.Tools => Strings.Tools.GetLocalized(),
			WindowsShellPropertyPageKind.Hardware => Strings.Hardware.GetLocalized(),
			WindowsShellPropertyPageKind.Shortcut => Strings.Shortcut.GetLocalized(),
			WindowsShellPropertyPageKind.Compatibility => Strings.Compatibility.GetLocalized(),
			WindowsShellPropertyPageKind.Sharing => Strings.Sharing.GetLocalized(),
			WindowsShellPropertyPageKind.Security => Strings.Security.GetLocalized(),
			WindowsShellPropertyPageKind.PreviousVersions => Strings.PreviousVersions.GetLocalized(),
			WindowsShellPropertyPageKind.Quota => Strings.Quota.GetLocalized(),
			WindowsShellPropertyPageKind.Customize => Strings.Customize.GetLocalized(),
			WindowsShellPropertyPageKind.DigitalSignatures => Strings.DigitalSignatures.GetLocalized(),
			WindowsShellPropertyPageKind.Details => Strings.Details.GetLocalized(),
			_ => string.Format(CultureInfo.CurrentCulture, Strings.PropertyPageFallbackFormat.GetLocalized(), index + 1),
		};
	}

	private void LaunchSystemTool(string fileName, string? argument)
	{
		try
		{
			var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = true };
			if (!string.IsNullOrEmpty(argument))
			{
				startInfo.ArgumentList.Add(argument);
			}

			Process.Start(startInfo);
		}
		catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			ShowError(exception.Message);
		}
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
