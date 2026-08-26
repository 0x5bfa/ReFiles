// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;
using Files.Controls;
using Files.Infrastructure;
using Files.Localization;
using Files.Core.Sessions;
using Files.Core.Browsing;
using Files.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Files.ViewModels;

public sealed class TabViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
	private const string SettingsIconGlyph = "\uE713";
	private const string SettingsIconResourceKey = "App.ThemedIcons.Settings";

	private readonly TabSession _tab;

	private readonly IUIDispatcher _dispatcher;
	private readonly WindowPresentationFactory _presentationFactory;

	private readonly WindowCommandManager _commandManager;

	private readonly Dictionary<Guid, PaneViewModel> _paneViewModels = [];

	private int _isDisposed;

	private int _refreshQueued;

	private string? _operationError;

	private bool _isRefreshing;

	public Guid Id => _tab.Id;

	public ObservableCollection<PaneViewModel> Panes { get; }

	public PaneViewModel? ActivePane => Panes.FirstOrDefault(static pane => pane.IsActive);

	public PaneSplitOrientation SplitOrientation => _tab.SplitOrientation;

	public bool IsSettings => _tab.ActivePane?.Content is SettingsPaneSession;

	public string Title => IsSettings ? Strings.Settings.GetLocalized() : ActivePane?.Title ?? Strings.NewTab.GetLocalized();

	public BitmapImage? Icon => ActivePane?.Icon;

	public IconSource? IconSource => IsSettings ? CreateSettingsIconSource() : Icon is { } icon ? new ImageIconSource { ImageSource = icon } : null;

	public string StatusText => _operationError ?? (IsSettings ? string.Empty : ActivePane?.FolderBrowser.StatusText ?? Strings.NoPane.GetLocalized());

	public bool IsLoading => ActivePane?.IsLoading ?? false;

	public bool CanGoBack => ActivePane?.CanGoBack ?? false;

	public bool CanGoForward => ActivePane?.CanGoForward ?? false;

	public bool CanGoUp => ActivePane?.CanGoUp ?? false;

	public bool CanRefresh => ActivePane?.CanRefresh ?? false;

	public bool CanClosePane => Panes.Count > 1;

	public bool CanOpenPane => !IsSettings && Panes.Count < 2;

	public CommandBindingViewModel NewTabCommand => _commandManager.GetBinding(CommandIds.NewTab);

	public CommandBindingViewModel DuplicateTabCommand => _commandManager.GetBinding(CommandIds.DuplicateTab);

	public CommandBindingViewModel MoveTabToNewWindowCommand => _commandManager.GetBinding(CommandIds.MoveTabToNewWindow);

	public CommandBindingViewModel CloseTabsToLeftCommand => _commandManager.GetBinding(CommandIds.CloseTabsToLeft);

	public CommandBindingViewModel CloseTabsToRightCommand => _commandManager.GetBinding(CommandIds.CloseTabsToRight);

	public CommandBindingViewModel CloseOtherTabsCommand => _commandManager.GetBinding(CommandIds.CloseOtherTabs);

	public CommandBindingViewModel ReopenTabCommand => _commandManager.GetBinding(CommandIds.ReopenTab);

	public BrowseLocation? Location => ActivePane?.FolderBrowser.Location;

	internal TabViewModel(TabSession tab, WindowPresentationFactory presentationFactory, WindowCommandManager commandManager)
	{
		ArgumentNullException.ThrowIfNull(tab);
		ArgumentNullException.ThrowIfNull(presentationFactory);
		ArgumentNullException.ThrowIfNull(commandManager);

		_tab = tab;
		_presentationFactory = presentationFactory;
		_dispatcher = presentationFactory.Dispatcher;
		_commandManager = commandManager;
		Panes = [];

		tab.PanesChanged += Tab_StateChanged;
		tab.ActivePaneChanged += Tab_StateChanged;
		tab.SplitOrientationChanged += Tab_StateChanged;
		RefreshFromCore();
	}

	public async Task OpenPaneAsync(PaneSplitOrientation orientation, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		if (IsSettings)
		{
			throw new NotSupportedException("Settings tabs cannot be split.");
		}

		await _tab.OpenSplitAsync(orientation, cancellationToken: cancellationToken).ConfigureAwait(false);
	}

	public bool CanSplitPane(PaneSplitOrientation orientation)
	{
		EnsureActive();

		return !IsSettings && (orientation is PaneSplitOrientation.Vertical or PaneSplitOrientation.Horizontal)
			&& (CanOpenPane || SplitOrientation != orientation);
	}

	public bool SetSplitOrientation(PaneSplitOrientation orientation)
	{
		EnsureActive();

		return _tab.SetSplitOrientation(orientation);
	}

	public async Task CloseActivePaneAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		if (ActivePane is not { } activePane)
		{
			return;
		}

		await _tab.ClosePaneAsync(activePane.Id, cancellationToken).ConfigureAwait(false);
	}

	public bool SetActivePane(Guid paneId)
	{
		EnsureActive();

		return _tab.SetActivePane(paneId);
	}

	public void ReportOperationError(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);

		_operationError = exception.Message;
		OnPropertyChanged(nameof(StatusText));
	}

	public void Dispose()
	{
		_ = DisposeAsync();
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_tab.PanesChanged -= Tab_StateChanged;
		_tab.ActivePaneChanged -= Tab_StateChanged;
		_tab.SplitOrientationChanged -= Tab_StateChanged;

		foreach (var pane in _paneViewModels.Values.ToArray())
		{
			pane.PropertyChanged -= PaneViewModel_PropertyChanged;
			await pane.DisposeAsync();
		}

		_paneViewModels.Clear();
		Panes.Clear();
	}

	private void Tab_StateChanged(object? sender, EventArgs args)
	{
		if (Interlocked.Exchange(ref _refreshQueued, 1) is not 0)
		{
			return;
		}

		if (!_dispatcher.TryEnqueue(() =>
		{
			Interlocked.Exchange(ref _refreshQueued, 0);
			RefreshFromCore();
		}))
		{
			Interlocked.Exchange(ref _refreshQueued, 0);
			if (Volatile.Read(ref _isDisposed) is 0)
			{
				throw new InvalidOperationException("The Files UI dispatcher rejected a tab update.");
			}
		}
	}

	private void RefreshFromCore()
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || _isRefreshing)
		{
			return;
		}

		_isRefreshing = true;

		try
		{
			var corePanes = _tab.Panes;
			var corePaneIds = corePanes.Select(static pane => pane.Id).ToHashSet();

			foreach (var removedId in _paneViewModels.Keys .Where(id => !corePaneIds.Contains(id)) .ToArray())
			{
				var removedPane = _paneViewModels[removedId];
				removedPane.PropertyChanged -= PaneViewModel_PropertyChanged;
				removedPane.Dispose();
				_paneViewModels.Remove(removedId);
			}

			foreach (var corePane in corePanes.Where(static pane => pane.Content is not SettingsPaneSession))
			{
				if (!_paneViewModels.ContainsKey(corePane.Id))
				{
					var paneViewModel = _presentationFactory.CreatePane(corePane, _commandManager);
					paneViewModel.PropertyChanged += PaneViewModel_PropertyChanged;
					_paneViewModels[corePane.Id] = paneViewModel;
				}
			}

			var orderedPanes = corePanes.Where(static pane => pane.Content is not SettingsPaneSession).Select(corePane => _paneViewModels[corePane.Id]).ToArray();
			foreach (var pane in orderedPanes)
			{
				pane.SetActive(_tab.ActivePane?.Id == pane.Id);
			}

			ObservableCollectionSynchronizer.Synchronize(Panes, orderedPanes);

			_operationError = null;
			OnPropertyChanged(nameof(ActivePane));
			OnPropertyChanged(nameof(SplitOrientation));
			OnPropertyChanged(nameof(Title));
			OnPropertyChanged(nameof(Icon));
			OnPropertyChanged(nameof(IconSource));
			OnPropertyChanged(nameof(StatusText));
			OnPropertyChanged(nameof(CanClosePane));
			OnPropertyChanged(nameof(CanOpenPane));
			OnPropertyChanged(nameof(IsLoading));
			OnPropertyChanged(nameof(CanGoBack));
			OnPropertyChanged(nameof(CanGoForward));
			OnPropertyChanged(nameof(CanGoUp));
			OnPropertyChanged(nameof(CanRefresh));
		}
		finally
		{
			_isRefreshing = false;
		}
	}

	private void PaneViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is not PaneViewModel pane || !ReferenceEquals(pane, ActivePane))
		{
			return;
		}

		switch (e.PropertyName)
		{
			case nameof(PaneViewModel.Title):
				OnPropertyChanged(nameof(Title));
				break;
			case nameof(PaneViewModel.Icon):
				OnPropertyChanged(nameof(Icon));
				OnPropertyChanged(nameof(IconSource));
				break;
			case nameof(PaneViewModel.StatusText):
				OnPropertyChanged(nameof(StatusText));
				break;
			case nameof(PaneViewModel.IsLoading):
				OnPropertyChanged(nameof(IsLoading));
				break;
			case nameof(PaneViewModel.CanGoBack):
				OnPropertyChanged(nameof(CanGoBack));
				break;
			case nameof(PaneViewModel.CanGoForward):
				OnPropertyChanged(nameof(CanGoForward));
				break;
			case nameof(PaneViewModel.CanGoUp):
				OnPropertyChanged(nameof(CanGoUp));
				break;
			case nameof(PaneViewModel.CanRefresh):
				OnPropertyChanged(nameof(CanRefresh));
				break;
		}
	}

	private static IconSource CreateSettingsIconSource()
	{
		return Application.Current?.Resources.TryGetValue(SettingsIconResourceKey, out var value) is true && value is ThemedIconData iconData
			? new ThemedIconSource { IconData = iconData, IconSize = 16 }
			: new FontIconSource { Glyph = SettingsIconGlyph };
	}

	private void EnsureActive() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

}
