// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Adapters;
using Files.Commands;
using Files.Localization;
using Files.ViewModels;
using Files.Core.Windows;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Files.Views;

internal sealed class ItemContextMenu
{
	private const double ShellIconSize = 16;
	private const int MaximumShellIconPixelSize = 256;

	private readonly FolderBrowserViewModel _viewModel;
	private readonly IReadOnlyList<BrowseItemViewModel> _selection;
	private readonly MenuFlyout _flyout = new() { AreOpenCloseAnimationsEnabled = false };
	private readonly CancellationTokenSource _lifetime = new();
	private readonly List<(ImageIcon Icon, WindowsShellContextMenuEntry Command)> _pendingShellIcons = [];
	private readonly List<WindowsShellContextMenuEntry> _applicationEntries = [];
	private readonly Dictionary<string, (CommandBindingViewModel Binding, object? Parameter, string Glyph)> _applicationCommands = [];
	private readonly object? _location;
	private WindowsShellContextMenuEntry? _expandedMenuEntry;
	private WindowsShellContextMenuSession? _session;
	private FrameworkElement? _target;
	private Task _invocationTask = Task.CompletedTask;
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
		_selection = selection.ToArray();
		_location = viewModel.Location;
		AddCommand(CommandIds.OpenItem, invokedItem, "\uE8E5");
		AddCommand(CommandIds.Cut, null, "\uE8C6", "Ctrl+X");
		AddCommand(CommandIds.Copy, null, "\uE8C8", "Ctrl+C");
		AddCommand(CommandIds.Delete, null, "\uE74D", "Del");
		AddCommand(CommandIds.Properties, null, "\uE946", "Alt+Enter");
		if (_viewModel.CanShowShellContextMenu)
		{
			_expandedMenuEntry = WindowsShellContextMenuEntry.CreateApplicationCommand("Files.ContextMenu.ShowMoreOptions", Strings.ShowMoreOptions.GetLocalized(), true);
		}

		_flyout.Closed += Flyout_Closed;
	}

	internal void ShowAt(FrameworkElement target, Point? position)
	{
		ArgumentNullException.ThrowIfNull(target);

		_target = target;
		target.Unloaded += Target_Unloaded;
		var invocationPoint = position ?? new Point(target.ActualWidth / 2, target.ActualHeight / 2);
		_classicMenuPosition = target.TransformToVisual(null).TransformPoint(invocationPoint);
		_rasterizationScale = target.XamlRoot?.RasterizationScale ?? 1;

		_ = LoadAndShowAsync(target, position, _lifetime.Token);
	}

	internal void Dismiss()
	{
		_flyout.Hide();
		Close();
	}

	private void AddCommand(CommandId id, object? parameter, string glyph, string? shortcut = null)
	{
		var binding = _viewModel.CommandManager.GetBinding(id);
		if (!binding.IsVisible)
		{
			return;
		}

		_applicationCommands.Add(id.Value, (binding, parameter, glyph));
		_applicationEntries.Add(WindowsShellContextMenuEntry.CreateApplicationCommand(id.Value, binding.Label, binding.IsEnabled, shortcut));
	}

	private async Task LoadAndShowAsync(FrameworkElement target, Point? position, CancellationToken cancellationToken)
	{
		IReadOnlyList<WindowsShellContextMenuEntry> commands = WindowsShellContextMenuBuilder.BuildApplicationMenu(_applicationEntries, _expandedMenuEntry);
		Exception? loadException = null;
		try
		{
			_session = await _viewModel.CreateShellContextMenuAsync(_selection, _applicationEntries, _expandedMenuEntry,
				_classicMenuPosition, _rasterizationScale, Windows.Win32.PInvoke.GetKeyState(0x10) < 0, cancellationToken).ConfigureAwait(false);
			if (_isClosed || cancellationToken.IsCancellationRequested)
			{
				if (_session is not null)
				{
					await _session.DisposeAsync().ConfigureAwait(false);
				}

				return;
			}

			commands = _session?.Entries ?? commands;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return;
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			loadException = exception;
		}

		try
		{
			await RunOnUiAsync(() =>
			{
				if (_isClosed || cancellationToken.IsCancellationRequested || !target.IsLoaded || !Equals(_location, _viewModel.Location)
					|| !_selection.Select(static item => item.Reference).SequenceEqual(_viewModel.SelectedItems.Select(static item => item.Reference)))
				{
					Close();

					return;
				}

				InsertShellCommands(commands);
				if (position is { } point)
				{
					_flyout.ShowAt(target, new FlyoutShowOptions { Position = point });
				}
				else
				{
					_flyout.ShowAt(target);
				}

				StartShellIconLoads();
				if (loadException is not null)
				{
					_viewModel.ReportOperationError(loadException);
				}
			}).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			try
			{
				await RunOnUiAsync(() =>
				{
					Close();
					_viewModel.ReportOperationError(exception);
				}).ConfigureAwait(false);
			}
			catch
			{
				if (_session is not null)
				{
					await _session.DisposeAsync().ConfigureAwait(false);
				}
			}
		}
	}

	private void InsertShellCommands(IReadOnlyList<WindowsShellContextMenuEntry> commands)
	{
		foreach (var command in commands)
		{
			_flyout.Items.Add(CreateShellItem(command));
		}
	}

	private MenuFlyoutItemBase CreateShellItem(WindowsShellContextMenuEntry command)
	{
		if (command.IsSeparator)
		{
			return new MenuFlyoutSeparator();
		}

		if (command.IsApplicationCommand)
		{
			var applicationItem = new MenuFlyoutItem { Text = command.Title, IsEnabled = command.IsEnabled, KeyboardAcceleratorTextOverride = command.Shortcut ?? string.Empty };
			if (command.Id == "Files.ContextMenu.ShowMoreOptions")
			{
				applicationItem.Icon = new FontIcon { Glyph = "\uE712" };
				applicationItem.Click += ShowMoreOptions_Click;
			}
			else if (_applicationCommands.TryGetValue(command.Id, out var application))
			{
				applicationItem.Command = application.Binding.Command;
				applicationItem.CommandParameter = application.Parameter;
				applicationItem.Icon = new FontIcon { Glyph = application.Glyph };
			}

			return applicationItem;
		}

		var displayTitle = command.Title;
		var accessKey = command.AccessKey;
		if (command.Children.Count is not 0)
		{
			var subItem = new MenuFlyoutSubItem { Text = displayTitle, Icon = CreateShellIcon(command), IsEnabled = command.IsEnabled };
			if (accessKey is not null)
			{
				subItem.AccessKey = accessKey;
			}

			foreach (var child in command.Children)
			{
				subItem.Items.Add(CreateShellItem(child));
			}

			return subItem;
		}

		MenuFlyoutItem item = command.IsChecked || command.IsRadio ? new ToggleMenuFlyoutItem { IsChecked = command.IsChecked } : new MenuFlyoutItem();
		item.Text = displayTitle;
		if (accessKey is not null)
		{
			item.AccessKey = accessKey;
		}

		item.Icon = CreateShellIcon(command);
		item.IsEnabled = command.IsEnabled;
		item.KeyboardAcceleratorTextOverride = command.Shortcut ?? string.Empty;
		item.Click += (_, _) => _invocationTask = InvokeShellAsync(command);

		return item;
	}

	private IconElement? CreateShellIcon(WindowsShellContextMenuEntry command)
	{
		if (!command.HasIcon)
		{
			return null;
		}

		var icon = new ImageIcon { Width = ShellIconSize, Height = ShellIconSize };
		_pendingShellIcons.Add((icon, command));

		return icon;
	}

	private void StartShellIconLoads()
	{
		if (_session is not { } session)
		{
			return;
		}

		var pixelSize = Math.Clamp((int)Math.Ceiling(ShellIconSize * _rasterizationScale), (int)ShellIconSize, MaximumShellIconPixelSize);
		var theme = _target?.ActualTheme is ElementTheme.Dark ? WindowsShellContextMenuIconTheme.Dark : WindowsShellContextMenuIconTheme.Light;
		if (new AccessibilitySettings().HighContrast)
		{
			var background = new UISettings().GetColorValue(UIColorType.Background);
			theme = 299 * background.R + 587 * background.G + 114 * background.B >= 128000
				? WindowsShellContextMenuIconTheme.HighContrastWhite : WindowsShellContextMenuIconTheme.HighContrastBlack;
		}

		foreach (var (icon, command) in _pendingShellIcons)
		{
			_ = ApplyShellIconAsync(icon, session.GetIconAsync(command, pixelSize, theme, _lifetime.Token));
		}

		_pendingShellIcons.Clear();
	}

	private async Task ApplyShellIconAsync(ImageIcon icon, Task<ReadOnlyMemory<byte>> iconLoad)
	{
		try
		{
			var iconData = await iconLoad.ConfigureAwait(false);
			if (iconData.IsEmpty || _isClosed)
			{
				return;
			}

			var iconImage = await RunOnUiAsync(() => CreateShellImageAsync(iconData)).ConfigureAwait(false);
			if (_isClosed)
			{
				return;
			}

			await RunOnUiAsync(() =>
			{
				if (!_isClosed)
				{
					icon.Source = iconImage;
				}
			}).ConfigureAwait(false);
		}
		catch
		{
			// App-extension icons are optional.
		}
	}

	private static async Task<ImageSource> CreateShellImageAsync(ReadOnlyMemory<byte> iconData)
	{
		if (iconData.Span[..Math.Min(iconData.Length, 256)].IndexOf("<svg"u8) < 0)
		{
			return await ThumbnailImageFactory.CreateAsync(iconData);
		}

		using var stream = new MemoryStream(iconData.ToArray(), writable: false);
		using var randomAccessStream = stream.AsRandomAccessStream();
		var image = new SvgImageSource();
		await image.SetSourceAsync(randomAccessStream);

		return image;
	}

	private async Task InvokeShellAsync(WindowsShellContextMenuEntry command)
	{
		try
		{
			if (_session is not { } session || !await session.InvokeAsync(command).ConfigureAwait(false))
			{
				throw new InvalidOperationException($"The Windows Shell command '{command.Title}' could not be invoked.");
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

	private Task<T> RunOnUiAsync<T>(Func<Task<T>> action)
	{
		if (_viewModel.Dispatcher.HasThreadAccess)
		{
			return action();
		}

		var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_viewModel.Dispatcher.TryEnqueue(async () =>
		{
			try
			{
				completion.SetResult(await action().ConfigureAwait(false));
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
		Close();
	}

	private void Target_Unloaded(object sender, RoutedEventArgs e)
	{
		_flyout.Hide();
		Close();
	}

	private void Close()
	{
		if (_isClosed)
		{
			return;
		}

		_isClosed = true;
		_flyout.Closed -= Flyout_Closed;
		if (_target is not null)
		{
			_target.Unloaded -= Target_Unloaded;
		}

		_lifetime.Cancel();
		_ = CloseAsync();
	}

	private async Task CloseAsync()
	{
		try
		{
			await _invocationTask.ConfigureAwait(false);
			if (_session is not null)
			{
				await _session.DisposeAsync().ConfigureAwait(false);
			}

			if (_showClassicMenuRequested)
			{
				await ShowClassicMenuAsync().ConfigureAwait(false);
			}
		}
		catch (Exception exception)
		{
			await RunOnUiAsync(() => _viewModel.ReportOperationError(exception)).ConfigureAwait(false);
		}
		finally
		{
			_lifetime.Dispose();
		}
	}

}
