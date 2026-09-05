// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Adapters;
using Files.Commands;
using Files.Localization;
using Files.ViewModels;
using Files.Core.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace Files.Views;

internal sealed class ItemContextMenu
{
	private const double AppExtensionIconSize = 16;
	private const int MaximumAppExtensionIconPixelSize = 256;

	private readonly FolderBrowserViewModel _viewModel;
	private readonly IReadOnlyList<BrowseItemViewModel> _selection;
	private readonly MenuFlyout _flyout = new() { AreOpenCloseAnimationsEnabled = false };
	private readonly MenuFlyoutItem _loadingItem = new() { IsEnabled = false };
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Dictionary<AppExtensionIconCacheKey, Task<ReadOnlyMemory<byte>>> _appExtensionIconLoads = [];
	private Point _classicMenuPosition;
	private double _rasterizationScale = 1;
	private bool _showClassicMenuRequested;
	private bool _isClosed;

	internal ItemContextMenu(FolderBrowserViewModel viewModel, BrowseItemViewModel invokedItem, IReadOnlyList<BrowseItemViewModel> selection)
	{
		ArgumentNullException.ThrowIfNull(viewModel);
		ArgumentNullException.ThrowIfNull(invokedItem);
		ArgumentNullException.ThrowIfNull(selection);

		_viewModel = viewModel;
		_selection = selection;
		_loadingItem.Text = Strings.Loading.GetLocalized();
		AddCommand(CommandIds.OpenItem, invokedItem, "\uE8E5");
		_flyout.Items.Add(_loadingItem);
		_flyout.Items.Add(new MenuFlyoutSeparator());
		AddCommand(CommandIds.Cut, null, "\uE8C6", "Ctrl+X");
		AddCommand(CommandIds.Copy, null, "\uE8C8", "Ctrl+C");
		_flyout.Items.Add(new MenuFlyoutSeparator());
		AddCommand(CommandIds.Delete, null, "\uE74D", "Del");
		_flyout.Items.Add(new MenuFlyoutSeparator());
		AddCommand(CommandIds.Properties, null, "\uE946", "Alt+Enter");
		if (_viewModel.CanShowShellContextMenu)
		{
			_flyout.Items.Add(new MenuFlyoutSeparator());
			var showMoreOptions = new MenuFlyoutItem { Text = Strings.ShowMoreOptions.GetLocalized(), Icon = new FontIcon { Glyph = "\uE712" } };
			showMoreOptions.Click += ShowMoreOptions_Click;
			_flyout.Items.Add(showMoreOptions);
		}

		_flyout.Closed += Flyout_Closed;
	}

	internal void ShowAt(FrameworkElement target, Point? position)
	{
		ArgumentNullException.ThrowIfNull(target);
		var invocationPoint = position ?? new Point(target.ActualWidth / 2, target.ActualHeight / 2);
		_classicMenuPosition = target.TransformToVisual(null).TransformPoint(invocationPoint);
		_rasterizationScale = target.XamlRoot?.RasterizationScale ?? 1;

		if (position is { } point)
		{
			_flyout.ShowAt(target, new FlyoutShowOptions { Position = point });
		}
		else
		{
			_flyout.ShowAt(target);
		}

		_ = LoadAppExtensionsAsync(_lifetime.Token);
	}

	private void AddCommand(CommandId id, object? parameter, string glyph, string? shortcut = null)
	{
		var binding = _viewModel.CommandManager.GetBinding(id);
		if (!binding.IsVisible)
		{
			return;
		}

		var item = new MenuFlyoutItem
		{
			Text = binding.Label,
			Command = binding.Command,
			CommandParameter = parameter,
			Icon = new FontIcon { Glyph = glyph },
			IsEnabled = binding.IsEnabled,
		};
		if (!string.IsNullOrWhiteSpace(shortcut))
		{
			item.KeyboardAcceleratorTextOverride = shortcut;
		}

		_flyout.Items.Add(item);
	}

	private async Task LoadAppExtensionsAsync(CancellationToken cancellationToken)
	{
		try
		{
			var commands = await _viewModel.GetAppExtensionCommandsAsync(_selection, cancellationToken).ConfigureAwait(false);
			await RunOnUiAsync(() => InsertAppExtensionCommands(commands)).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			await RunOnUiAsync(() =>
			{
				RemoveLoadingItem();
				_viewModel.ReportOperationError(exception);
			}).ConfigureAwait(false);
		}
	}

	private void InsertAppExtensionCommands(IReadOnlyList<WindowsShellAppExtensionCommand> commands)
	{
		if (_isClosed)
		{
			return;
		}

		var insertionIndex = _flyout.Items.IndexOf(_loadingItem);
		if (insertionIndex < 0)
		{
			return;
		}

		_flyout.Items.RemoveAt(insertionIndex);
		foreach (var command in commands)
		{
			_flyout.Items.Insert(insertionIndex++, CreateAppExtensionItem(command));
		}
	}

	private MenuFlyoutItemBase CreateAppExtensionItem(WindowsShellAppExtensionCommand command)
	{
		if (command.IsSeparator)
		{
			return new MenuFlyoutSeparator();
		}

		if (command.Children.Count is not 0)
		{
			var subItem = new MenuFlyoutSubItem { Text = command.Title, Icon = CreateAppExtensionIcon(command), IsEnabled = command.IsEnabled };
			foreach (var child in command.Children)
			{
				subItem.Items.Add(CreateAppExtensionItem(child));
			}

			return subItem;
		}

		MenuFlyoutItem item = command.IsChecked || command.IsRadio ? new ToggleMenuFlyoutItem { IsChecked = command.IsChecked } : new MenuFlyoutItem();
		item.Text = command.Title;
		item.Icon = CreateAppExtensionIcon(command);
		item.IsEnabled = command.IsEnabled;
		item.Click += (_, _) => _ = InvokeAppExtensionAsync(command);

		return item;
	}

	private IconElement? CreateAppExtensionIcon(WindowsShellAppExtensionCommand command)
	{
		if (command.IconPath is not { Length: > 0 } iconPath)
		{
			return null;
		}

		var pixelSize = Math.Clamp((int)Math.Ceiling(AppExtensionIconSize * _rasterizationScale), (int)AppExtensionIconSize, MaximumAppExtensionIconPixelSize);
		var cacheKey = new AppExtensionIconCacheKey(iconPath, command.IconIndex, pixelSize);
		if (!_appExtensionIconLoads.TryGetValue(cacheKey, out var iconLoad))
		{
			iconLoad = _viewModel.GetAppExtensionIconAsync(command, pixelSize, _lifetime.Token);
			_appExtensionIconLoads.Add(cacheKey, iconLoad);
		}

		var icon = new ImageIcon { Width = AppExtensionIconSize, Height = AppExtensionIconSize };
		_ = ApplyAppExtensionIconAsync(icon, iconLoad);

		return icon;
	}

	private async Task ApplyAppExtensionIconAsync(ImageIcon icon, Task<ReadOnlyMemory<byte>> iconLoad)
	{
		try
		{
			var iconData = await iconLoad.ConfigureAwait(false);
			if (iconData.IsEmpty || _isClosed)
			{
				return;
			}

			await RunOnUiAsync(() =>
			{
				if (!_isClosed)
				{
					icon.Source = ThumbnailImageFactory.Create(iconData);
				}
			}).ConfigureAwait(false);
		}
		catch
		{
			// App-extension icons are optional.
		}
	}

	private async Task InvokeAppExtensionAsync(WindowsShellAppExtensionCommand command)
	{
		try
		{
			if (!await _viewModel.InvokeAppExtensionCommandAsync(_selection, command).ConfigureAwait(false))
			{
				throw new InvalidOperationException($"The File Explorer app extension '{command.Title}' could not be invoked.");
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			await RunOnUiAsync(() => _viewModel.ReportOperationError(exception)).ConfigureAwait(false);
		}
	}

	private Task RunOnUiAsync(Action action)
	{
		if (_viewModel.Dispatcher.HasThreadAccess)
		{
			action();

			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_viewModel.Dispatcher.TryEnqueue(() =>
		{
			try
			{
				action();
				completion.SetResult();
			}
			catch (Exception exception)
			{
				completion.SetException(exception);
			}
		}))
		{
			completion.SetException(new InvalidOperationException("The Files UI dispatcher rejected a context-menu update."));
		}

		return completion.Task;
	}

	private void RemoveLoadingItem()
	{
		if (_flyout.Items.Contains(_loadingItem))
		{
			_flyout.Items.Remove(_loadingItem);
		}
	}

	private void ShowMoreOptions_Click(object sender, RoutedEventArgs e)
	{
		_showClassicMenuRequested = true;
	}

	private async Task ShowClassicMenuAsync()
	{
		try
		{
			var target = await _viewModel.GetShellContextMenuTargetAsync(_selection).ConfigureAwait(false);
			if (target is null)
			{
				return;
			}

			await RunOnUiAsync(() => _viewModel.ShowShellContextMenu(target, _classicMenuPosition, _rasterizationScale)).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			await RunOnUiAsync(() => _viewModel.ReportOperationError(exception)).ConfigureAwait(false);
		}
	}

	private void Flyout_Closed(object? sender, object e)
	{
		_isClosed = true;
		_flyout.Closed -= Flyout_Closed;
		_lifetime.Cancel();
		_lifetime.Dispose();
		if (_showClassicMenuRequested)
		{
			_ = ShowClassicMenuAsync();
		}
	}

	private readonly record struct AppExtensionIconCacheKey(string Path, int Index, int PixelSize);
}
