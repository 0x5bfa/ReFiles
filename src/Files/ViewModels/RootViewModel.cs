// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Adapters;
using Files.Commands;
using Files.Infrastructure;
using Files.Localization;
using Files.Core.AppModels;
using Files.Core.Browsing;
using Files.Core.Data;

namespace Files.ViewModels;

public sealed class RootViewModel : ObservableObject, IDisposable
{
	private readonly WindowModel _window;

	private readonly IFilesDataRoot _dataRoot;

	private readonly IUIDispatcher _dispatcher;

	private readonly WindowCommandManager _commandManager;

	private readonly NavigationItemLoader _navigationItemLoader;

	private readonly CancellationTokenSource _lifetime = new();

	private readonly Dictionary<Guid, TabViewModel> _tabViewModels = [];

	private readonly Dictionary<int, NavigationItemViewModel> _navigationSectionViewModels = [];

	private readonly SemaphoreSlim _navigationThumbnailGate = new(4);

	private string? _operationError;

	private int _isDisposed;

	private int _navigationItemsStarted;

	private int _refreshQueued;

	private bool _isRefreshing;

	public ObservableCollection<TabViewModel> Tabs { get; }

	public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

	public NavigationItemViewModel HomeNavigationItem { get; }

	public TabStripViewModel TabStrip { get; }

	public NavigationToolbarViewModel NavigationToolbar { get; }

	public ToolbarViewModel Toolbar { get; }

	public WindowCommandManager Commands => _commandManager;

	public CommandBindingViewModel BackCommand =>
		_commandManager.GetBinding(CommandIds.NavigateBack);

	public CommandBindingViewModel ForwardCommand =>
		_commandManager.GetBinding(CommandIds.NavigateForward);

	public CommandBindingViewModel UpCommand =>
		_commandManager.GetBinding(CommandIds.NavigateUp);

	public CommandBindingViewModel HomeCommand =>
		_commandManager.GetBinding(CommandIds.NavigateHome);

	public CommandBindingViewModel NavigatePathCommand =>
		_commandManager.GetBinding(CommandIds.NavigatePath);

	public CommandBindingViewModel RefreshCommand =>
		_commandManager.GetBinding(CommandIds.Refresh);

	public CommandBindingViewModel NewTabCommand =>
		_commandManager.GetBinding(CommandIds.NewTab);

	public CommandBindingViewModel CloseTabCommand =>
		_commandManager.GetBinding(CommandIds.CloseTab);

	public CommandBindingViewModel NewPaneCommand =>
		_commandManager.GetBinding(CommandIds.NewPane);

	public CommandBindingViewModel ClosePaneCommand =>
		_commandManager.GetBinding(CommandIds.ClosePane);

	public CommandBindingViewModel LayoutDetailsCommand =>
		_commandManager.GetBinding(CommandIds.LayoutDetails);

	public CommandBindingViewModel LayoutListCommand =>
		_commandManager.GetBinding(CommandIds.LayoutList);

	public CommandBindingViewModel LayoutGridCommand =>
		_commandManager.GetBinding(CommandIds.LayoutGrid);

	internal IUIDispatcher Dispatcher => _dispatcher;

	public TabViewModel? ActiveTab =>
		Tabs.FirstOrDefault(tab => tab.Id == _window.ActiveTab?.Id);

	public FolderBrowserViewModel? ActiveFolderBrowser =>
		ActiveTab?.ActivePane?.FolderBrowser;

	public string StatusText =>
		_operationError
		?? ActiveTab?.StatusText
		?? Strings.NoTabs.GetLocalized();

	public RootViewModel(WindowModel window, IFilesDataRoot dataRoot, IUIDispatcher dispatcher, CommandRegistry commandRegistry)
	{
		ArgumentNullException.ThrowIfNull(window);
		ArgumentNullException.ThrowIfNull(dataRoot);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(commandRegistry);

		_window = window;
		_dataRoot = dataRoot;
		_dispatcher = dispatcher;
		_navigationItemLoader = new NavigationItemLoader(dataRoot);
		Tabs = [];
		NavigationItems = [];
		HomeNavigationItem = NavigationItemViewModel.CreateHome(Strings.Home.GetLocalized());
		NavigationItems.Add(HomeNavigationItem);
		_commandManager = new WindowCommandManager(this, commandRegistry, dispatcher);
		TabStrip = new(Tabs, NewTabCommand, CloseTabCommand, SetActiveTabAt);
		NavigationToolbar = new(BackCommand, ForwardCommand, UpCommand, HomeCommand, NavigatePathCommand, RefreshCommand);
		Toolbar = new(NewPaneCommand, ClosePaneCommand, LayoutDetailsCommand, LayoutListCommand, LayoutGridCommand);

		window.StateChanged += Window_StateChanged;
		RefreshFromCore();
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		cancellationToken.ThrowIfCancellationRequested();

		if (Interlocked.Exchange(ref _navigationItemsStarted, 1) is 0)
		{
			_ = LoadNavigationItemsForInitializationAsync(cancellationToken, _lifetime.Token);
		}

		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

		if (ActiveTab?.ActivePane is { } pane)
		{
			await pane.FolderBrowser.InitializeAsync(linkedCancellation.Token).ConfigureAwait(false);
		}
	}

	public Task NavigateToNavigationItemAsync(NavigationItemViewModel item, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		ArgumentNullException.ThrowIfNull(item);

		if (!item.SelectsOnInvoked || item.Reference is not { } reference || ActiveFolderBrowser is not { } browser)
		{
			return Task.CompletedTask;
		}

		return browser.NavigateToReferenceAsync(reference, cancellationToken);
	}

	public async Task OpenTabAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		await _window.OpenTabAsync(HomeLocation.Instance, cancellationToken).ConfigureAwait(false);
	}

	public async Task CloseTabAsync(Guid tabId, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		if (Tabs.Count <= 1)
		{
			return;
		}

		await _window.CloseTabAsync(tabId, cancellationToken).ConfigureAwait(false);
	}

	public bool SetActiveTab(Guid tabId)
	{
		EnsureActive();

		return _window.SetActiveTab(tabId);
	}

	public void SetActiveTabAt(int index)
	{
		if (index >= 0 && index < Tabs.Count)
		{
			SetActiveTab(Tabs[index].Id);
		}
	}

	public void ReportOperationError(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);

		_operationError = exception.Message;
		OnPropertyChanged(nameof(StatusText));
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_lifetime.Cancel();
		_window.StateChanged -= Window_StateChanged;
		NavigationToolbar.Dispose();
		Toolbar.Dispose();
		_commandManager.Dispose();
		foreach (var tab in _tabViewModels.Values)
		{
			tab.PropertyChanged -= TabViewModel_PropertyChanged;
			tab.Dispose();
		}

		_tabViewModels.Clear();
		Tabs.Clear();
		_navigationSectionViewModels.Clear();
		NavigationItems.Clear();
		_navigationThumbnailGate.Dispose();
		_lifetime.Dispose();
	}

	private async Task<bool> LoadNavigationItemsAsync(CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var section in _navigationItemLoader .LoadSectionsAsync(cancellationToken) .ConfigureAwait(false))
			{
				await ApplyNavigationSectionOnUiAsync(section).ConfigureAwait(false);
			}

			return true;
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		catch (Exception exception)
		{
			ReportNavigationLoadError(exception);

			return false;
		}
	}

	private async Task LoadNavigationItemsForInitializationAsync(CancellationToken initializationCancellationToken, CancellationToken lifetimeCancellationToken)
	{
		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(initializationCancellationToken, lifetimeCancellationToken);
		var completed = false;
		try
		{
			completed = await LoadNavigationItemsAsync(linkedCancellation.Token).ConfigureAwait(false);
		}
		finally
		{
			if (!completed && !lifetimeCancellationToken.IsCancellationRequested)
			{
				Interlocked.Exchange(ref _navigationItemsStarted, 0);
			}
		}
	}

	private Task ApplyNavigationSectionOnUiAsync(NavigationSectionData section)
	{
		if (_dispatcher.HasThreadAccess)
		{
			ApplyNavigationSection(section);

			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_dispatcher.TryEnqueue(() => { try { ApplyNavigationSection(section); completion.SetResult(true); } catch (Exception exception) { completion.SetException(exception); } }))
		{
			completion.SetException(new InvalidOperationException("The Files UI dispatcher rejected navigation items."));
		}

		return completion.Task;
	}

	private void ApplyNavigationSection(NavigationSectionData section)
	{
		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		if (_navigationSectionViewModels.Remove(section.Order, out var previousSection))
		{
			NavigationItems.Remove(previousSection);
		}

		var children = new List<NavigationItemViewModel>(section.Items.Count);
		var navigationCancellationToken = _lifetime.Token;
		foreach (var item in section.Items)
		{
			var child = NavigationItemViewModel.CreateFolder(item.Name, item.Reference);
			children.Add(child);
			_ = Task.Run(() => LoadNavigationThumbnailAsync(item, child, navigationCancellationToken));
		}

		var sectionViewModel = NavigationItemViewModel.CreateSection(section.Name, section.Reference, children);
		var insertIndex = 1;
		foreach (var order in _navigationSectionViewModels.Keys)
		{
			if (order < section.Order)
			{
				insertIndex++;
			}
		}

		NavigationItems.Insert(insertIndex, sectionViewModel);
		_navigationSectionViewModels.Add(section.Order, sectionViewModel);
	}

	private async Task LoadNavigationThumbnailAsync(NavigationItemData item, NavigationItemViewModel viewModel, CancellationToken cancellationToken)
	{
		try
		{
			await _navigationThumbnailGate.WaitAsync(cancellationToken).ConfigureAwait(false);

			try
			{
				var thumbnail = await _navigationItemLoader.LoadThumbnailAsync(item.Reference, cancellationToken).ConfigureAwait(false);
				if (thumbnail is null || Volatile.Read(ref _isDisposed) is not 0)
				{
					return;
				}

				await SetNavigationThumbnailOnUiAsync(viewModel, thumbnail).ConfigureAwait(false);
			}
			finally
			{
				_navigationThumbnailGate.Release();
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception)
		{
			// Shell thumbnail loading is best effort.
		}
	}

	private Task SetNavigationThumbnailOnUiAsync(NavigationItemViewModel viewModel, byte[] thumbnail)
	{
		if (_dispatcher.HasThreadAccess)
		{
			return SetNavigationThumbnailAsync(viewModel, thumbnail);
		}

		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_dispatcher.TryEnqueue(async () => { try { await SetNavigationThumbnailAsync(viewModel, thumbnail); completion.SetResult(true); } catch (Exception exception) { completion.SetException(exception); } }))
		{
			completion.SetException(new InvalidOperationException("The Files UI dispatcher rejected a navigation thumbnail."));
		}

		return completion.Task;
	}

	private static async Task SetNavigationThumbnailAsync(NavigationItemViewModel viewModel, byte[] thumbnail)
	{
		viewModel.SetThumbnail(await ThumbnailImageFactory .CreateAsync(thumbnail) .ConfigureAwait(true));
	}

	private void ReportNavigationLoadError(Exception exception)
	{
		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		if (_dispatcher.HasThreadAccess)
		{
			ReportOperationError(exception);

			return;
		}

		_dispatcher.TryEnqueue(() => {if (Volatile.Read(ref _isDisposed) is 0) {ReportOperationError(exception);}});
	}

	private void Window_StateChanged(object? sender, EventArgs args)
	{
		if (Interlocked.Exchange(ref _refreshQueued, 1) is not 0)
		{
			return;
		}

		if (!_dispatcher.TryEnqueue(() => {Interlocked.Exchange(ref _refreshQueued, 0); RefreshFromCore();}))
		{
			Interlocked.Exchange(ref _refreshQueued, 0);
			if (Volatile.Read(ref _isDisposed) is 0)
			{
				throw new InvalidOperationException("The Files UI dispatcher rejected a window update.");
			}
		}
	}

	private void TabViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(TabViewModel.StatusText) or nameof(TabViewModel.ActivePane) or nameof(TabViewModel.Title) or nameof(TabViewModel.CanClosePane))
		{
			OnPropertyChanged(nameof(StatusText));
			OnPropertyChanged(nameof(ActiveFolderBrowser));
			NavigationToolbar.SetActiveFolderBrowser(ActiveFolderBrowser);
			Toolbar.SetActiveTab(ActiveTab);
			_commandManager.RefreshStates();
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
			var coreTabs = _window.Tabs;
			var coreTabIds = coreTabs.Select(static tab => tab.Id).ToHashSet();

			foreach (var removedId in _tabViewModels.Keys .Where(id => !coreTabIds.Contains(id)) .ToArray())
			{
				var removedTab = _tabViewModels[removedId];
				removedTab.PropertyChanged -= TabViewModel_PropertyChanged;
				removedTab.Dispose();
				_tabViewModels.Remove(removedId);
			}

			foreach (var coreTab in coreTabs)
			{
				if (!_tabViewModels.ContainsKey(coreTab.Id))
				{
					var tabViewModel = new TabViewModel(coreTab, _dataRoot, _dispatcher, _commandManager);
					tabViewModel.PropertyChanged += TabViewModel_PropertyChanged;
					_tabViewModels[coreTab.Id] = tabViewModel;
				}
			}

			var orderedTabs = coreTabs.Select(coreTab => _tabViewModels[coreTab.Id]).ToArray();
			ObservableCollectionSynchronizer.Synchronize(Tabs, orderedTabs);

			var activeTabId = _window.ActiveTab?.Id;
			var activeTabIndex = activeTabId is { } id
				? Tabs.Select((tab, index) => (tab, index)).FirstOrDefault(value => value.tab.Id == id).index
				: -1;
			TabStrip.SetActiveTabIndex(activeTabIndex);

			_operationError = null;
			OnPropertyChanged(nameof(ActiveTab));
			OnPropertyChanged(nameof(ActiveFolderBrowser));
			NavigationToolbar.SetActiveFolderBrowser(ActiveFolderBrowser);
			Toolbar.SetActiveTab(ActiveTab);
			OnPropertyChanged(nameof(StatusText));
			_commandManager.RefreshStates();
		}
		finally
		{
			_isRefreshing = false;
		}
	}

	private void EnsureActive()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);
	}
}
