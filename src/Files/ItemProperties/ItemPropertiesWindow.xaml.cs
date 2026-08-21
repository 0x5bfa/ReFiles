// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.IO;
using Files.Adapters;
using Files.Core.ItemFeatures.Thumbnails;
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
	private readonly Func<CancellationToken, Task<WindowsShellPropertySheetData?>>? _getPropertySheetData;
	private readonly Func<CancellationToken, Task<(string? Description, ThumbnailResult? Icon)>>? _getGeneralProperties;
	private bool _isInitialized;

	internal ItemPropertiesViewModel ViewModel { get; }

	internal ItemPropertiesWindow(
		IReadOnlyList<BrowseItemViewModel> items,
		Func<CancellationToken, Task<WindowsShellPropertySheetData?>>? getPropertySheetData = null,
		Func<CancellationToken, Task<(string? Description, ThumbnailResult? Icon)>>? getGeneralProperties = null)
	{
		ViewModel = new(items);
		_getPropertySheetData = getPropertySheetData;
		_getGeneralProperties = getGeneralProperties;
		InitializeComponent();
		Title = ViewModel.WindowTitle;
		AppWindow.Resize(new SizeInt32(540, 650));
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
			await Task.WhenAll(ViewModel.InitializeAsync(_lifetime.Token), PopulatePropertyTabsAsync(_lifetime.Token), PopulateGeneralPropertiesAsync(_lifetime.Token));
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

	private async void AdvancedButton_Click(object sender, RoutedEventArgs e)
	{
		var archive = new CheckBox
		{
			Content = ViewModel.ArchiveLabel,
			IsChecked = ViewModel.IsArchive,
			IsThreeState = true,
		};
		var content = new StackPanel { MinWidth = 320, Spacing = 12 };
		content.Children.Add(archive);
		CheckBox? applyToContents = null;
		if (ViewModel.HasFolders)
		{
			applyToContents = new CheckBox
			{
				Content = ViewModel.ApplyToContentsLabel,
				IsChecked = ViewModel.ApplyToContents,
			};
			content.Children.Add(applyToContents);
		}

		var dialog = new ContentDialog
		{
			Title = ViewModel.AdvancedAttributesLabel,
			Content = content,
			PrimaryButtonText = ViewModel.OkLabel,
			CloseButtonText = ViewModel.CancelLabel,
			DefaultButton = ContentDialogButton.Primary,
			XamlRoot = Content.XamlRoot,
		};
		if (await dialog.ShowAsync() is ContentDialogResult.Primary)
		{
			ViewModel.IsArchive = archive.IsChecked;
			ViewModel.ApplyToContents = applyToContents?.IsChecked is true;
		}
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
		if (_getPropertySheetData is null)
		{
			return;
		}

		var data = await _getPropertySheetData(cancellationToken);
		if (data is null || data.Pages.Count is 0)
		{
			return;
		}

		ViewModel.SetShellDetails(data.Details);
		PropertyTabs.TabItems.Clear();
		for (var index = 0; index < data.Pages.Count; index++)
		{
			var page = data.Pages[index];
			var title = GetPageTitle(page, index);
			PropertyTabs.TabItems.Add(page.Kind switch
			{
				WindowsShellPropertyPageKind.General => PrepareExistingPage(GeneralTab, title),
				WindowsShellPropertyPageKind.Shortcut => CreateShortcutPage(title, data.Shortcut),
				WindowsShellPropertyPageKind.Sharing => CreateSharingPage(title, data.Sharing),
				WindowsShellPropertyPageKind.Security => CreateSecurityPage(title, data.Security),
				WindowsShellPropertyPageKind.PreviousVersions => CreatePreviousVersionsPage(title, data.PreviousVersions),
				WindowsShellPropertyPageKind.Customize => CreateCustomizePage(title, data.Customization),
				WindowsShellPropertyPageKind.DigitalSignatures => CreateDigitalSignaturesPage(title, data.EmbeddedSignatures, data.CatalogSignatures),
				WindowsShellPropertyPageKind.Details => PrepareExistingPage(DetailsTab, title),
				_ => CreateMessagePage(title, Strings.Unspecified.GetLocalized()),
			});
		}
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

	private static TabViewItem PrepareExistingPage(TabViewItem page, string title)
	{
		page.Header = title;

		return page;
	}

	private static TabViewItem CreateShortcutPage(string title, WindowsShellShortcutProperties? shortcut)
	{
		if (shortcut is null)
		{
			return CreateMessagePage(title, Strings.Unspecified.GetLocalized());
		}

		var content = CreatePageStack();
		content.Children.Add(CreatePropertyRows((Strings.TargetType.GetLocalized(), shortcut.TargetType), (Strings.TargetLocation.GetLocalized(), shortcut.TargetLocation)));
		content.Children.Add(CreateReadOnlyField(Strings.Target.GetLocalized(), FormatShortcutTarget(shortcut)));
		content.Children.Add(CreateReadOnlyField(Strings.StartIn.GetLocalized(), shortcut.WorkingDirectory));
		content.Children.Add(CreateReadOnlyField(Strings.ShortcutKey.GetLocalized(), FormatHotkey(shortcut.Hotkey)));
		content.Children.Add(CreateReadOnlyField(Strings.Run.GetLocalized(), FormatShowCommand(shortcut.ShowCommand)));
		content.Children.Add(CreateReadOnlyField(Strings.Comment.GetLocalized(), shortcut.Comment));
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
		buttons.Children.Add(new Button { Content = Strings.OpenFileLocation.GetLocalized(), IsEnabled = false });
		buttons.Children.Add(new Button { Content = Strings.ChangeIcon.GetLocalized(), IsEnabled = false });
		buttons.Children.Add(new Button { Content = Strings.Advanced.GetLocalized(), IsEnabled = false });
		content.Children.Add(buttons);

		return CreatePage(title, content);
	}

	private static TabViewItem CreateSharingPage(string title, WindowsShellSharingProperties? sharing)
	{
		if (sharing is null)
		{
			return CreateMessagePage(title, Strings.Unspecified.GetLocalized());
		}

		var content = CreatePageStack();
		var sharingContent = new StackPanel { Spacing = 10 };
		sharingContent.Children.Add(new TextBlock { Text = sharing.IsShared ? Strings.Shared.GetLocalized() : Strings.NotShared.GetLocalized() });
		sharingContent.Children.Add(CreatePropertyRows((Strings.NetworkPath.GetLocalized(), sharing.NetworkPath)));
		sharingContent.Children.Add(new Button { HorizontalAlignment = HorizontalAlignment.Left, Content = Strings.Share.GetLocalized(), IsEnabled = false });
		content.Children.Add(CreateSection(Strings.NetworkFolderSharing.GetLocalized(), sharingContent));
		var advancedContent = new StackPanel { Spacing = 10 };
		advancedContent.Children.Add(new Button { HorizontalAlignment = HorizontalAlignment.Left, Content = Strings.AdvancedSharing.GetLocalized(), IsEnabled = false });
		content.Children.Add(CreateSection(Strings.AdvancedSharing.GetLocalized(), advancedContent));
		content.Children.Add(CreateSection(Strings.PasswordProtection.GetLocalized(), new TextBlock { Text = Strings.PasswordProtectionDescription.GetLocalized(), TextWrapping = TextWrapping.Wrap }));

		return CreatePage(title, content);
	}

	private static TabViewItem CreateSecurityPage(string title, WindowsShellSecurityProperties? security)
	{
		if (security is null)
		{
			return CreateMessagePage(title, Strings.Unspecified.GetLocalized());
		}

		var content = CreatePageStack();
		content.Children.Add(CreatePropertyRows((Strings.ObjectName.GetLocalized(), security.ObjectPath)));
		content.Children.Add(new TextBlock { Text = Strings.GroupOrUserNames.GetLocalized() });
		var principalList = new ListView
		{
			Height = 160,
			ItemsSource = security.Principals.Select(static principal => principal.Name).ToArray(),
			SelectionMode = ListViewSelectionMode.Single,
		};
		content.Children.Add(principalList);
		var permissions = new StackPanel { Spacing = 6 };
		content.Children.Add(permissions);
		void updatePermissions(int selectedIndex)
		{
			permissions.Children.Clear();
			if (selectedIndex < 0 || selectedIndex >= security.Principals.Count)
			{
				return;
			}

			var principal = security.Principals[selectedIndex];
			permissions.Children.Add(new TextBlock
			{
				FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
				Text = string.Format(CultureInfo.CurrentCulture, Strings.PermissionsForFormat.GetLocalized(), principal.Name),
			});
			permissions.Children.Add(CreatePermissionHeader());
			permissions.Children.Add(CreatePermissionRow(Strings.FullControl.GetLocalized(), principal, 0x000F01FF));
			permissions.Children.Add(CreatePermissionRow(Strings.Modify.GetLocalized(), principal, 0x000301BF));
			permissions.Children.Add(CreatePermissionRow(Strings.ReadAndExecute.GetLocalized(), principal, 0x000200A9));
			permissions.Children.Add(CreatePermissionRow(Strings.ListFolderContents.GetLocalized(), principal, 0x000200A9));
			permissions.Children.Add(CreatePermissionRow(Strings.ReadPermission.GetLocalized(), principal, 0x00020089));
			permissions.Children.Add(CreatePermissionRow(Strings.WritePermission.GetLocalized(), principal, 0x00000116));
		}

		principalList.SelectionChanged += (_, _) => updatePermissions(principalList.SelectedIndex);
		if (security.Principals.Count is not 0)
		{
			principalList.SelectedIndex = 0;
		}

		var advanced = new Grid { ColumnSpacing = 8 };
		advanced.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
		advanced.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
		advanced.Children.Add(new TextBlock { Text = Strings.SecurityAdvancedDescription.GetLocalized(), TextWrapping = TextWrapping.Wrap });
		var advancedButton = new Button { Content = Strings.Advanced.GetLocalized(), IsEnabled = false };
		Grid.SetColumn(advancedButton, 1);
		advanced.Children.Add(advancedButton);
		content.Children.Add(advanced);

		return CreatePage(title, content);
	}

	private static TabViewItem CreatePreviousVersionsPage(string title, IReadOnlyList<WindowsShellPreviousVersion> versions)
	{
		var content = CreatePageStack();
		content.Children.Add(new TextBlock { Text = Strings.PreviousVersionsDescription.GetLocalized(), TextWrapping = TextWrapping.Wrap });
		content.Children.Add(new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Text = Strings.FolderVersions.GetLocalized() });
		if (versions.Count is 0)
		{
			content.Children.Add(new TextBlock { Margin = new Thickness(0, 24, 0, 24), HorizontalAlignment = HorizontalAlignment.Center, Text = Strings.NoPreviousVersions.GetLocalized() });
		}
		else
		{
			content.Children.Add(new ListView { Height = 260, ItemsSource = versions.Select(static version => $"{version.DateModified:g}    {version.Name}").ToArray() });
		}

		var buttons = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Orientation = Orientation.Horizontal, Spacing = 8 };
		buttons.Children.Add(new Button { Content = Strings.Open.GetLocalized(), IsEnabled = false });
		buttons.Children.Add(new Button { Content = Strings.Restore.GetLocalized(), IsEnabled = false });
		content.Children.Add(buttons);

		return CreatePage(title, content);
	}

	private static TabViewItem CreateCustomizePage(string title, WindowsShellFolderCustomizationProperties? customization)
	{
		if (customization is null)
		{
			return CreateMessagePage(title, Strings.Unspecified.GetLocalized());
		}

		var content = CreatePageStack();
		var kindContent = new StackPanel { Spacing = 8 };
		kindContent.Children.Add(new TextBlock { Text = Strings.OptimizeFolderFor.GetLocalized() });
		kindContent.Children.Add(new ComboBox
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			IsEnabled = false,
			ItemsSource = new[] { string.IsNullOrEmpty(customization.FolderKind) ? Strings.GeneralItems.GetLocalized() : customization.FolderKind },
			SelectedIndex = 0,
		});
		kindContent.Children.Add(new CheckBox { Content = Strings.ApplyTemplateToSubfolders.GetLocalized(), IsEnabled = false });
		content.Children.Add(CreateSection(Strings.FolderCustomizationQuestion.GetLocalized(), kindContent));
		var pictureContent = new StackPanel { Spacing = 8 };
		pictureContent.Children.Add(CreatePropertyRows((Strings.Location.GetLocalized(), customization.PicturePath)));
		pictureContent.Children.Add(CreateDisabledButtons(Strings.ChooseFile.GetLocalized(), Strings.RestoreDefault.GetLocalized()));
		content.Children.Add(CreateSection(Strings.FolderPictures.GetLocalized(), pictureContent));
		var iconContent = new StackPanel { Spacing = 8 };
		iconContent.Children.Add(CreatePropertyRows((Strings.Location.GetLocalized(), customization.IconPath)));
		iconContent.Children.Add(new Button { HorizontalAlignment = HorizontalAlignment.Left, Content = Strings.ChangeIcon.GetLocalized(), IsEnabled = false });
		content.Children.Add(CreateSection(Strings.FolderIcons.GetLocalized(), iconContent));

		return CreatePage(title, content);
	}

	private static TabViewItem CreateDigitalSignaturesPage(string title, IReadOnlyList<WindowsShellDigitalSignature> embeddedSignatures, IReadOnlyList<WindowsShellDigitalSignature> catalogSignatures)
	{
		var content = CreatePageStack();
		content.Children.Add(CreateSignatureSection(Strings.EmbeddedSignatures.GetLocalized(), embeddedSignatures));
		content.Children.Add(CreateSignatureSection(Strings.CatalogSignatures.GetLocalized(), catalogSignatures));

		return CreatePage(title, content);
	}

	private static UIElement CreateSignatureSection(string title, IReadOnlyList<WindowsShellDigitalSignature> signatures)
	{
		var rows = new StackPanel { Spacing = 6 };
		if (signatures.Count is 0)
		{
			rows.Children.Add(new TextBlock { Margin = new Thickness(8, 24, 8, 24), HorizontalAlignment = HorizontalAlignment.Center, Text = Strings.NoSignatures.GetLocalized() });
		}
		else
		{
			rows.Children.Add(CreateSignatureRow(Strings.SignerName.GetLocalized(), Strings.DigestAlgorithm.GetLocalized(), Strings.Timestamp.GetLocalized(), true));
			foreach (var signature in signatures)
			{
				var thirdColumn = string.IsNullOrEmpty(signature.CatalogPath) ? signature.Timestamp : Path.GetFileName(signature.CatalogPath);
				rows.Children.Add(CreateSignatureRow(signature.Signer, signature.DigestAlgorithm, thirdColumn, false));
			}
		}

		return CreateSection(title, rows);
	}

	private static Grid CreateSignatureRow(string signer, string algorithm, string thirdColumn, bool isHeader)
	{
		var row = new Grid { ColumnSpacing = 12 };
		row.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) });
		row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
		row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
		AddGridText(row, signer, 0, isHeader);
		AddGridText(row, algorithm, 1, isHeader);
		AddGridText(row, thirdColumn, 2, isHeader);

		return row;
	}

	private static Grid CreatePermissionHeader()
	{
		var header = CreatePermissionGrid();
		AddGridText(header, Strings.Allow.GetLocalized(), 1, true);
		AddGridText(header, Strings.Deny.GetLocalized(), 2, true);

		return header;
	}

	private static Grid CreatePermissionRow(string label, WindowsShellSecurityPrincipal principal, uint mask)
	{
		var row = CreatePermissionGrid();
		AddGridText(row, label, 0, false);
		AddGridText(row, HasPermission(principal.AllowedAccessMask, mask) ? "\uE73E" : string.Empty, 1, false, "Segoe Fluent Icons");
		AddGridText(row, HasPermission(principal.DeniedAccessMask, mask) ? "\uE73E" : string.Empty, 2, false, "Segoe Fluent Icons");

		return row;
	}

	private static Grid CreatePermissionGrid()
	{
		var grid = new Grid { ColumnSpacing = 12 };
		grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new() { Width = new GridLength(64) });
		grid.ColumnDefinitions.Add(new() { Width = new GridLength(64) });

		return grid;
	}

	private static bool HasPermission(uint actual, uint required)
	{
		return (actual & required) == required;
	}

	private static void AddGridText(Grid grid, string text, int column, bool isHeader, string? fontFamily = null)
	{
		var value = new TextBlock
		{
			FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
			Text = text,
			TextTrimming = TextTrimming.CharacterEllipsis,
		};
		if (fontFamily is not null)
		{
			value.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(fontFamily);
		}

		Grid.SetColumn(value, column);
		grid.Children.Add(value);
	}

	private static UIElement CreatePropertyRows(params (string Label, string Value)[] rows)
	{
		var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
		grid.ColumnDefinitions.Add(new() { Width = new GridLength(120) });
		grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
		for (var index = 0; index < rows.Length; index++)
		{
			grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
			var label = new TextBlock { Text = FormatPropertyLabel(rows[index].Label) };
			var value = new TextBlock { IsTextSelectionEnabled = true, Text = rows[index].Value, TextTrimming = TextTrimming.CharacterEllipsis };
			Grid.SetRow(label, index);
			Grid.SetRow(value, index);
			Grid.SetColumn(value, 1);
			grid.Children.Add(label);
			grid.Children.Add(value);
		}

		return grid;
	}

	private static UIElement CreateReadOnlyField(string label, string value)
	{
		var grid = new Grid { ColumnSpacing = 12 };
		grid.ColumnDefinitions.Add(new() { Width = new GridLength(120) });
		grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
		grid.Children.Add(new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = FormatPropertyLabel(label) });
		var field = new TextBox { IsReadOnly = true, Text = value };
		Grid.SetColumn(field, 1);
		grid.Children.Add(field);

		return grid;
	}

	private static UIElement CreateDisabledButtons(params string[] labels)
	{
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
		foreach (var label in labels)
		{
			buttons.Children.Add(new Button { Content = label, IsEnabled = false });
		}

		return buttons;
	}

	private static UIElement CreateSection(string title, UIElement content)
	{
		var section = new StackPanel { Spacing = 8 };
		section.Children.Add(new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Text = title });
		section.Children.Add(content);

		return new Border
		{
			BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
			BorderThickness = new Thickness(1),
			Child = section,
			Padding = new Thickness(10),
		};
	}

	private static StackPanel CreatePageStack()
	{
		return new StackPanel { Spacing = 14 };
	}

	private static TabViewItem CreatePage(string title, UIElement content)
	{
		return new TabViewItem { Header = title, IsClosable = false, Content = new ScrollViewer { Padding = new Thickness(14, 12, 14, 8), Content = content } };
	}

	private static TabViewItem CreateMessagePage(string title, string message)
	{
		var content = new TextBlock
		{
			Margin = new Thickness(24),
			MaxWidth = 480,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Text = message,
			TextAlignment = TextAlignment.Center,
			TextWrapping = TextWrapping.Wrap,
		};

		return new TabViewItem { Header = title, IsClosable = false, Content = content };
	}

	private static string FormatShortcutTarget(WindowsShellShortcutProperties shortcut)
	{
		var target = shortcut.TargetPath.Contains(' ') ? $"\"{shortcut.TargetPath}\"" : shortcut.TargetPath;

		return string.IsNullOrWhiteSpace(shortcut.Arguments) ? target : $"{target} {shortcut.Arguments}";
	}

	private static string FormatHotkey(ushort hotkey)
	{
		if (hotkey is 0)
		{
			return Strings.None.GetLocalized();
		}

		var parts = new List<string>();
		var modifiers = hotkey >> 8;
		if ((modifiers & 2) is not 0)
		{
			parts.Add(Strings.ControlKey.GetLocalized());
		}

		if ((modifiers & 4) is not 0)
		{
			parts.Add(Strings.AltKey.GetLocalized());
		}

		if ((modifiers & 1) is not 0)
		{
			parts.Add(Strings.ShiftKey.GetLocalized());
		}

		var key = hotkey & 0xFF;
		parts.Add(key is >= 0x30 and <= 0x5A ? ((char)key).ToString() : $"0x{key:X2}");

		return string.Join(" + ", parts);
	}

	private static string FormatShowCommand(int showCommand)
	{
		return showCommand switch
		{
			3 => Strings.Maximized.GetLocalized(),
			7 => Strings.Minimized.GetLocalized(),
			_ => Strings.NormalWindow.GetLocalized(),
		};
	}

	private static string FormatPropertyLabel(string label)
	{
		return string.Format(CultureInfo.CurrentCulture, Strings.PropertyLabelFormat.GetLocalized(), label);
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
			WindowsShellPropertyPageKind.Shortcut => Strings.Shortcut.GetLocalized(),
			WindowsShellPropertyPageKind.Sharing => Strings.Sharing.GetLocalized(),
			WindowsShellPropertyPageKind.Security => Strings.Security.GetLocalized(),
			WindowsShellPropertyPageKind.PreviousVersions => Strings.PreviousVersions.GetLocalized(),
			WindowsShellPropertyPageKind.Customize => Strings.Customize.GetLocalized(),
			WindowsShellPropertyPageKind.DigitalSignatures => Strings.DigitalSignatures.GetLocalized(),
			WindowsShellPropertyPageKind.Details => Strings.Details.GetLocalized(),
			_ => string.Format(CultureInfo.CurrentCulture, Strings.PropertyPageFallbackFormat.GetLocalized(), index + 1),
		};
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
