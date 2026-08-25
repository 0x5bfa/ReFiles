// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;
using Files.Adapters;
using Files.Activation;
using Files.Commands;
using Files.Controls;
using Files.Infrastructure;
using Files.Localization;
using Files.Core.Sessions;
using Files.Core.Browsing;
using Files.Presentation;
using Files.ItemProperties;
using System.Collections.Specialized;

namespace Files.ViewModels;

public sealed partial class RootViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
	private readonly WindowSession _window;

	private readonly WindowPresentationFactory _presentationFactory;

	private readonly IUIDispatcher _dispatcher;

	private readonly WindowCommandManager _commandManager;

	private readonly NavigationItemLoader _navigationItemLoader;

	private readonly CancellationTokenSource _lifetime = new();

	private readonly Dictionary<Guid, TabViewModel> _tabViewModels = [];

	private readonly Stack<ClosedTab> _closedTabs = [];

	private readonly Lock _closedTabsLock = new();

	private readonly Dictionary<int, NavigationItemViewModel> _navigationSectionViewModels = [];

	private readonly HashSet<NavigationItemViewModel> _trackedNavigationItems = [];

	private readonly SemaphoreSlim _navigationThumbnailGate = new(4);

	private string? _operationError;

	private SidebarDisplayMode _sidebarDisplayMode = SidebarDisplayMode.Expanded;

	private int _isDisposed;

	private int _navigationItemsStarted;

	private int _refreshQueued;

	private bool _isRefreshing;

	public ObservableCollection<TabViewModel> Tabs { get; }

	public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

	public ObservableCollection<FlatSidebarItem> SidebarItems { get; }

	public ObservableCollection<FlatSidebarItem> SidebarFooterItems { get; }

	public SidebarDisplayMode SidebarDisplayMode
	{
		get => _sidebarDisplayMode;
		set
		{
			if (!SetProperty(ref _sidebarDisplayMode, value))
			{
				return;
			}

			_commandManager.RefreshStates(CommandStateInvalidation.Pane);
		}
	}

	public NavigationItemViewModel HomeNavigationItem { get; }

	public NavigationItemViewModel SettingsNavigationItem { get; }

	public TabStripViewModel TabStrip { get; }

	public NavigationToolbarViewModel NavigationToolbar { get; }

	public ToolbarViewModel Toolbar { get; }

	public WindowCommandManager Commands => _commandManager;

	public CommandBindingViewModel BackCommand => _commandManager.GetBinding(CommandIds.NavigateBack);

	public CommandBindingViewModel ToggleSidebarCommand => _commandManager.GetBinding(CommandIds.ToggleSidebar);

	public CommandBindingViewModel ForwardCommand => _commandManager.GetBinding(CommandIds.NavigateForward);

	public CommandBindingViewModel UpCommand => _commandManager.GetBinding(CommandIds.NavigateUp);

	public CommandBindingViewModel HomeCommand => _commandManager.GetBinding(CommandIds.NavigateHome);

	public CommandBindingViewModel NavigatePathCommand => _commandManager.GetBinding(CommandIds.NavigatePath);

	public CommandBindingViewModel RefreshCommand => _commandManager.GetBinding(CommandIds.Refresh);

	public CommandBindingViewModel CopyCommand => _commandManager.GetBinding(CommandIds.Copy);

	public CommandBindingViewModel CutCommand => _commandManager.GetBinding(CommandIds.Cut);

	public CommandBindingViewModel PasteCommand => _commandManager.GetBinding(CommandIds.Paste);

	public CommandBindingViewModel DeleteCommand => _commandManager.GetBinding(CommandIds.Delete);

	public CommandBindingViewModel NewTabCommand => _commandManager.GetBinding(CommandIds.NewTab);

	public CommandBindingViewModel CloseTabCommand => _commandManager.GetBinding(CommandIds.CloseTab);

	public CommandBindingViewModel NewPaneCommand => _commandManager.GetBinding(CommandIds.NewPane);

	public CommandBindingViewModel ClosePaneCommand => _commandManager.GetBinding(CommandIds.ClosePane);

	public CommandBindingViewModel SplitPaneVerticalCommand => _commandManager.GetBinding(CommandIds.SplitPaneVertical);

	public CommandBindingViewModel SplitPaneHorizontalCommand => _commandManager.GetBinding(CommandIds.SplitPaneHorizontal);

	public CommandBindingViewModel LayoutDetailsCommand => _commandManager.GetBinding(CommandIds.LayoutDetails);

	public CommandBindingViewModel LayoutListCommand => _commandManager.GetBinding(CommandIds.LayoutList);

	public CommandBindingViewModel LayoutCardsCommand => _commandManager.GetBinding(CommandIds.LayoutCards);

	public CommandBindingViewModel LayoutGridCommand => _commandManager.GetBinding(CommandIds.LayoutGrid);

	public CommandBindingViewModel LayoutColumnsCommand => _commandManager.GetBinding(CommandIds.LayoutColumns);

	public CommandBindingViewModel SortItemsCommand => _commandManager.GetBinding(CommandIds.SortItems);

	public CommandBindingViewModel GroupItemsCommand => _commandManager.GetBinding(CommandIds.GroupItems);

	internal IUIDispatcher Dispatcher => _dispatcher;

	internal IItemActivationService ItemActivationService => _presentationFactory.ItemActivationService;

	internal IItemPropertiesService? ItemPropertiesService => _presentationFactory.ItemPropertiesService;

	internal Func<Task>? CloseWindowAsync { get; set; }

	public TabViewModel? ActiveTab => Tabs.FirstOrDefault(tab => tab.Id == _window.ActiveTab?.Id);

	public FolderBrowserViewModel? ActiveFolderBrowser => ActiveTab?.ActivePane?.FolderBrowser;

	public PreviewPaneViewModel? ActivePreview => ActiveTab?.ActivePane?.Preview;

	public string StatusText => _operationError ?? ActiveTab?.StatusText ?? Strings.NoTabs.GetLocalized();

	internal bool CanReopenTab
	{
		get
		{
			lock (_closedTabsLock)
			{
				return _closedTabs.Count > 0;
			}
		}
	}

	internal RootViewModel(WindowSession window, WindowPresentationFactory presentationFactory)
	{
		ArgumentNullException.ThrowIfNull(window);
		ArgumentNullException.ThrowIfNull(presentationFactory);

		_window = window;
		_presentationFactory = presentationFactory;
		_dispatcher = presentationFactory.Dispatcher;
		_navigationItemLoader = presentationFactory.CreateNavigationItemLoader();
		Tabs = [];
		NavigationItems = [];
		SidebarItems = [];
		SidebarFooterItems = [];
		NavigationItems.CollectionChanged += NavigationItems_CollectionChanged;
		HomeNavigationItem = NavigationItemViewModel.CreateHome(Strings.Home.GetLocalized());
		SettingsNavigationItem = NavigationItemViewModel.CreateSettings(Strings.Settings.GetLocalized());
		NavigationItems.Add(HomeNavigationItem);
		SidebarFooterItems.Add(new FlatSidebarItem(SettingsNavigationItem, 0));
		_commandManager = presentationFactory.CreateCommandManager(this);
		TabStrip = new(
			Tabs,
			NewTabCommand,
			CloseTabCommand,
			NewPaneCommand,
			ClosePaneCommand,
			SplitPaneVerticalCommand,
			SplitPaneHorizontalCommand,
			SetActiveTabAt);
		NavigationToolbar = new(ToggleSidebarCommand, BackCommand, ForwardCommand, UpCommand, HomeCommand, NavigatePathCommand, RefreshCommand);
		Toolbar = new(
			CopyCommand,
			CutCommand,
			PasteCommand,
			DeleteCommand,
			SortItemsCommand,
			GroupItemsCommand,
			LayoutDetailsCommand,
			LayoutListCommand,
			LayoutCardsCommand,
			LayoutGridCommand,
			LayoutColumnsCommand);

		window.TabsChanged += Window_StateChanged;
		window.ActiveTabChanged += Window_StateChanged;
		RefreshFromCore();
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();
		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("RootViewModel", "Initialize START");

		cancellationToken.ThrowIfCancellationRequested();

		if (Interlocked.Exchange(ref _navigationItemsStarted, 1) is 0)
		{
			_ = LoadNavigationItemsForInitializationAsync(cancellationToken, _lifetime.Token);
		}

		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

		try
		{
			if (ActiveTab?.ActivePane is { } pane)
			{
				var folderStartTimestamp = Stopwatch.GetTimestamp();
				await pane.FolderBrowser.InitializeAsync(linkedCancellation.Token).ConfigureAwait(false);
				UiDiagnosticLog.Write("RootViewModel", $"Folder initialization END elapsedMs={Stopwatch.GetElapsedTime(folderStartTimestamp).TotalMilliseconds:F1}");
			}
		}
		finally
		{
			UiDiagnosticLog.Write("RootViewModel", $"Initialize END elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
		}
	}

	public async Task NavigateToNavigationItemAsync(NavigationItemViewModel item, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		ArgumentNullException.ThrowIfNull(item);

		if (ReferenceEquals(item, SettingsNavigationItem))
		{
			await OpenSettingsTabAsync(cancellationToken).ConfigureAwait(false);

			return;
		}

		if (!item.SelectsOnInvoked)
		{
			return;
		}

		if (ActiveFolderBrowser is { } browser)
		{
			if (item.IsHome)
			{
				await browser.NavigateHomeAsync(cancellationToken).ConfigureAwait(false);
			}
			else if (item.Reference is { } reference)
			{
				await browser.NavigateToReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
			}

			return;
		}

		if (item.IsHome)
		{
			await _window.OpenTabAsync(HomeLocation.Instance, cancellationToken).ConfigureAwait(false);
		}
		else if (item.Reference is { } reference)
		{
			await _window.OpenTabAsync(new FolderLocation(reference), cancellationToken).ConfigureAwait(false);
		}
	}

	public async Task OpenTabAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		await _window.OpenTabAsync(HomeLocation.Instance, cancellationToken).ConfigureAwait(false);
	}

	public async Task OpenSettingsTabAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		await _window.OpenContentTabAsync(static () => new SettingsPaneSession(), cancellationToken).ConfigureAwait(false);
	}

	public async Task CloseTabAsync(Guid tabId, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		if (Tabs.FirstOrDefault(tab => tab.Id == tabId) is not { } tab)
		{
			return;
		}

		if (Tabs.Count is 1)
		{
			if (CloseWindowAsync is { } closeWindowAsync)
			{
				await closeWindowAsync().ConfigureAwait(false);
			}

			return;
		}

		var closedTab = CreateClosedTab(tab);
		if (await _window.CloseTabAsync(tabId, cancellationToken).ConfigureAwait(false))
		{
			RememberClosedTab(closedTab);
		}
	}

	internal async Task DuplicateTabAsync(Guid tabId, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		if (GetTabIndex(tabId) < 0)
		{
			return;
		}

		var tab = Tabs.First(tab => tab.Id == tabId);
		if (tab.IsSettings)
		{
			await OpenSettingsTabAsync(cancellationToken).ConfigureAwait(false);

			return;
		}

		var location = tab.ActivePane?.FolderBrowser.Location ?? HomeLocation.Instance;
		await _window.OpenTabAsync(location, cancellationToken).ConfigureAwait(false);
	}

	internal async Task CloseTabsToLeftAsync(Guid tabId, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		var tabIndex = GetTabIndex(tabId);
		if (tabIndex <= 0)
		{
			return;
		}

		var tabsToClose = Tabs
			.Take(tabIndex)
			.Select(static tab => (tab.Id, Content: CreateClosedTab(tab)))
			.ToArray();
		await CloseTabsAsync(tabsToClose, cancellationToken).ConfigureAwait(false);
	}

	internal async Task CloseTabsToRightAsync(Guid tabId, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		var tabIndex = GetTabIndex(tabId);
		if (tabIndex < 0 || tabIndex >= Tabs.Count - 1)
		{
			return;
		}

		var tabsToClose = Tabs
			.Skip(tabIndex + 1)
			.Select(static tab => (tab.Id, Content: CreateClosedTab(tab)))
			.ToArray();
		await CloseTabsAsync(tabsToClose, cancellationToken).ConfigureAwait(false);
	}

	internal async Task CloseOtherTabsAsync(Guid tabId, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		if (GetTabIndex(tabId) < 0)
		{
			return;
		}

		var tabsToClose = Tabs
			.Where(tab => tab.Id != tabId)
			.Select(static tab => (tab.Id, Content: CreateClosedTab(tab)))
			.ToArray();
		await CloseTabsAsync(tabsToClose, cancellationToken).ConfigureAwait(false);
	}

	internal async Task ReopenTabAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		ClosedTab closedTab;
		lock (_closedTabsLock)
		{
			if (_closedTabs.Count is 0)
			{
				return;
			}

			closedTab = _closedTabs.Pop();
		}

		try
		{
			if (closedTab.IsSettings)
			{
				await OpenSettingsTabAsync(cancellationToken).ConfigureAwait(false);
			}
			else
			{
				await _window.OpenTabAsync(closedTab.Location, cancellationToken).ConfigureAwait(false);
			}
		}
		catch
		{
			RememberClosedTab(closedTab);
			throw;
		}
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

	internal int GetTabIndex(Guid tabId)
	{
		for (var index = 0; index < Tabs.Count; index++)
		{
			if (Tabs[index].Id == tabId)
			{
				return index;
			}
		}

		return -1;
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

		_lifetime.Cancel();
		_window.TabsChanged -= Window_StateChanged;
		_window.ActiveTabChanged -= Window_StateChanged;
		NavigationToolbar.Dispose();
		Toolbar.Dispose();
		_commandManager.Dispose();

		foreach (var tab in _tabViewModels.Values.ToArray())
		{
			tab.PropertyChanged -= TabViewModel_PropertyChanged;
			await tab.DisposeAsync();
		}

		_tabViewModels.Clear();
		Tabs.Clear();
		lock (_closedTabsLock)
		{
			_closedTabs.Clear();
		}

		_navigationSectionViewModels.Clear();
		NavigationItems.CollectionChanged -= NavigationItems_CollectionChanged;
		UnsubscribeNavigationItems();
		SidebarItems.Clear();
		SidebarFooterItems.Clear();
		NavigationItems.Clear();
		_navigationThumbnailGate.Dispose();
		_lifetime.Dispose();
	}

	private void NavigationItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		RebuildSidebarItems();
	}

	private void NavigationItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(NavigationItemViewModel.IsExpanded))
		{
			RebuildSidebarItems();
		}
	}

	private void RebuildSidebarItems()
	{
		UnsubscribeNavigationItems();
		SidebarItems.Clear();

		foreach (var item in NavigationItems)
		{
			AddSidebarItem(item, 0);
		}
	}

	private void AddSidebarItem(NavigationItemViewModel item, int depth)
	{
		if (_trackedNavigationItems.Add(item))
		{
			item.PropertyChanged += NavigationItem_PropertyChanged;
			item.Children.CollectionChanged += NavigationItems_CollectionChanged;
		}

		SidebarItems.Add(new FlatSidebarItem(item, depth));
		if (!item.IsExpanded)
		{
			return;
		}

		foreach (var child in item.Children)
		{
			AddSidebarItem(child, depth + 1);
		}
	}

	private void UnsubscribeNavigationItems()
	{
		foreach (var item in _trackedNavigationItems)
		{
			item.PropertyChanged -= NavigationItem_PropertyChanged;
			item.Children.CollectionChanged -= NavigationItems_CollectionChanged;
		}

		_trackedNavigationItems.Clear();
	}

	private async Task<bool> LoadNavigationItemsAsync(CancellationToken cancellationToken)
	{
		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("RootViewModel", "LoadNavigationItems START");
		try
		{
			await foreach (var section in _navigationItemLoader.LoadSectionsAsync(cancellationToken).ConfigureAwait(false))
			{
				await ApplyNavigationSectionOnUiAsync(section).ConfigureAwait(false);
				UiDiagnosticLog.Write("RootViewModel", $"Navigation section applied order={section.Order} items={section.Items.Count} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
			}

			UiDiagnosticLog.Write("RootViewModel", $"LoadNavigationItems END elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

			return true;
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		catch (Exception exception)
		{
			UiDiagnosticLog.Write(
				"RootViewModel",
				$"LoadNavigationItems ERROR type={exception.GetType().Name} message={exception.Message} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
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

		if (!_dispatcher.TryEnqueue(() =>
		{
			try
			{
				ApplyNavigationSection(section);
				completion.SetResult(true);
			}
			catch (Exception exception)
			{
				completion.SetException(exception);
			}
		}))
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

		var sectionViewModel = NavigationItemViewModel.CreateSection(section.SectionType, section.Name, section.Reference, children);
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
		UiDiagnosticLog.Write("RootViewModel", $"ApplyNavigationSection order={section.Order} items={section.Items.Count} navigationItems={NavigationItems.Count}");
	}

	private async Task LoadNavigationThumbnailAsync(NavigationItemData item, NavigationItemViewModel viewModel, CancellationToken cancellationToken)
	{
		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("RootViewModel", $"Navigation thumbnail START name={item.Name}");
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
				UiDiagnosticLog.Write("RootViewModel", $"Navigation thumbnail END name={item.Name} bytes={thumbnail.Length} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
			}
			finally
			{
				_navigationThumbnailGate.Release();
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			UiDiagnosticLog.Write("RootViewModel", $"Navigation thumbnail ERROR name={item.Name} type={exception.GetType().Name} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
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

		if (!_dispatcher.TryEnqueue(async () =>
		{
			try
			{
				await SetNavigationThumbnailAsync(viewModel, thumbnail);
				completion.SetResult(true);
			}
			catch (Exception exception)
			{
				completion.SetException(exception);
			}
		}))
		{
			completion.SetException(new InvalidOperationException("The Files UI dispatcher rejected a navigation thumbnail."));
		}

		return completion.Task;
	}

	private static async Task SetNavigationThumbnailAsync(NavigationItemViewModel viewModel, byte[] thumbnail)
	{
		var startTimestamp = Stopwatch.GetTimestamp();
		viewModel.SetThumbnail(await ThumbnailImageFactory .CreateAsync(thumbnail) .ConfigureAwait(true));
		UiDiagnosticLog.Write("RootViewModel", $"Navigation thumbnail decode END bytes={thumbnail.Length} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
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

		_dispatcher.TryEnqueue(() =>
		{
			if (Volatile.Read(ref _isDisposed) is 0)
			{
				ReportOperationError(exception);
			}
		});
	}

	private void Window_StateChanged(object? sender, EventArgs args)
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
				throw new InvalidOperationException("The Files UI dispatcher rejected a window update.");
			}
		}
	}

	private void TabViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (e.PropertyName)
		{
			case nameof(TabViewModel.StatusText):
				OnPropertyChanged(nameof(StatusText));
				break;
			case nameof(TabViewModel.ActivePane):
				OnPropertyChanged(nameof(StatusText));
				OnPropertyChanged(nameof(ActiveFolderBrowser));
				OnPropertyChanged(nameof(ActivePreview));
				NavigationToolbar.SetActiveFolderBrowser(ActiveFolderBrowser);
				Toolbar.SetActiveTab(ActiveTab);
				_commandManager.RefreshStates(CommandStateInvalidation.All);
				break;
			case nameof(TabViewModel.CanClosePane):
			case nameof(TabViewModel.CanOpenPane):
			case nameof(TabViewModel.SplitOrientation):
				_commandManager.RefreshStates(CommandStateInvalidation.Pane);
				break;
			case nameof(TabViewModel.IsLoading):
				_commandManager.RefreshStates(CommandStateInvalidation.Loading);
				break;
			case nameof(TabViewModel.CanGoBack):
			case nameof(TabViewModel.CanGoForward):
			case nameof(TabViewModel.CanGoUp):
				_commandManager.RefreshStates(CommandStateInvalidation.Navigation);
				break;
			case nameof(TabViewModel.CanRefresh):
				_commandManager.RefreshStates(CommandStateInvalidation.Loading);
				break;
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
					var tabViewModel = _presentationFactory.CreateTab(coreTab, _commandManager);
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
			OnPropertyChanged(nameof(ActivePreview));
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

	private async Task CloseTabsAsync((Guid Id, ClosedTab Content)[] tabs, CancellationToken cancellationToken)
	{
		foreach (var tab in tabs)
		{
			if (await _window.CloseTabAsync(tab.Id, cancellationToken).ConfigureAwait(false))
			{
				RememberClosedTab(tab.Content);
			}
		}
	}

	private static ClosedTab CreateClosedTab(TabViewModel tab)
	{
		return new ClosedTab(tab.IsSettings, tab.ActivePane?.FolderBrowser.Location ?? HomeLocation.Instance);
	}

	private void RememberClosedTab(ClosedTab closedTab)
	{
		lock (_closedTabsLock)
		{
			_closedTabs.Push(closedTab);
		}
	}

	private void EnsureActive()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);
	}

	private readonly record struct ClosedTab(bool IsSettings, BrowseLocation Location);
}
