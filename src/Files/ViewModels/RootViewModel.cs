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
	private readonly WindowModel window;
	private readonly IFilesDataRoot dataRoot;
	private readonly IUIDispatcher dispatcher;
	private readonly WindowCommandManager commandManager;
	private readonly NavigationItemLoader navigationItemLoader;
	private readonly CancellationTokenSource lifetime = new();
	private readonly Dictionary<Guid, TabViewModel> tabViewModels = [];
	private readonly Dictionary<int, NavigationItemViewModel> navigationSectionViewModels = [];
	private readonly SemaphoreSlim navigationThumbnailGate = new(4);
	private string? operationError;
	private int isDisposed;
	private int navigationItemsStarted;
	private int refreshQueued;
	private bool isRefreshing;

	public RootViewModel(WindowModel window, IFilesDataRoot dataRoot, IUIDispatcher dispatcher, CommandRegistry commandRegistry)
	{
		ArgumentNullException.ThrowIfNull(window);
		ArgumentNullException.ThrowIfNull(dataRoot);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(commandRegistry);

		this.window = window;
		this.dataRoot = dataRoot;
		this.dispatcher = dispatcher;
		navigationItemLoader = new NavigationItemLoader(dataRoot);
		Tabs = [];
		NavigationItems = [];
		HomeNavigationItem = NavigationItemViewModel.CreateHome(Strings.Home.GetLocalized());
		NavigationItems.Add(HomeNavigationItem);
		commandManager = new WindowCommandManager(this, commandRegistry, dispatcher);
		TabStrip = new(Tabs, NewTabCommand, CloseTabCommand, SetActiveTabAt);
		NavigationToolbar = new(BackCommand, ForwardCommand, UpCommand, HomeCommand, NavigatePathCommand, RefreshCommand);
		Toolbar = new(NewPaneCommand, ClosePaneCommand);

		window.StateChanged += Window_StateChanged;
		RefreshFromCore();
	}

	public ObservableCollection<TabViewModel> Tabs { get; }

	public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

	public NavigationItemViewModel HomeNavigationItem { get; }

	public TabStripViewModel TabStrip { get; }

	public NavigationToolbarViewModel NavigationToolbar { get; }

	public ToolbarViewModel Toolbar { get; }

	public WindowCommandManager Commands => commandManager;

	public CommandBindingViewModel BackCommand =>
		commandManager.GetBinding(CommandIds.NavigateBack);

	public CommandBindingViewModel ForwardCommand =>
		commandManager.GetBinding(CommandIds.NavigateForward);

	public CommandBindingViewModel UpCommand =>
		commandManager.GetBinding(CommandIds.NavigateUp);

	public CommandBindingViewModel HomeCommand =>
		commandManager.GetBinding(CommandIds.NavigateHome);

	public CommandBindingViewModel NavigatePathCommand =>
		commandManager.GetBinding(CommandIds.NavigatePath);

	public CommandBindingViewModel RefreshCommand =>
		commandManager.GetBinding(CommandIds.Refresh);

	public CommandBindingViewModel NewTabCommand =>
		commandManager.GetBinding(CommandIds.NewTab);

	public CommandBindingViewModel CloseTabCommand =>
		commandManager.GetBinding(CommandIds.CloseTab);

	public CommandBindingViewModel NewPaneCommand =>
		commandManager.GetBinding(CommandIds.NewPane);

	public CommandBindingViewModel ClosePaneCommand =>
		commandManager.GetBinding(CommandIds.ClosePane);

	internal IUIDispatcher Dispatcher => dispatcher;

	public TabViewModel? ActiveTab =>
		Tabs.FirstOrDefault(tab => tab.Id == window.ActiveTab?.Id);

	public FolderBrowserViewModel? ActiveFolderBrowser =>
		ActiveTab?.ActivePane?.FolderBrowser;

	public string StatusText =>
		operationError
		?? ActiveTab?.StatusText
		?? Strings.NoTabs.GetLocalized();

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();
		if (Interlocked.Exchange(ref navigationItemsStarted, 1) is 0)
		{
			var navigationCancellationToken = lifetime.Token;
			_ = Task.Run(() => LoadNavigationItemsAsync(navigationCancellationToken));
		}

		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);

		if (ActiveTab?.ActivePane is { } pane)
		{
			await pane.FolderBrowser
				.InitializeAsync(linkedCancellation.Token)
				.ConfigureAwait(false);
		}
	}

	public Task NavigateToNavigationItemAsync(NavigationItemViewModel item, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(item);

		if (!item.SelectsOnInvoked
			|| item.Reference is not { } reference
			|| ActiveFolderBrowser is not { } browser)
		{
			return Task.CompletedTask;
		}

		return browser.NavigateToReferenceAsync(reference, cancellationToken);
	}

	public async Task OpenTabAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();
		await window.OpenTabAsync(HomeLocation.Instance, cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task CloseTabAsync(Guid tabId, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		if (Tabs.Count <= 1)
		{
			return;
		}

		await window.CloseTabAsync(tabId, cancellationToken)
			.ConfigureAwait(false);
	}

	public bool SetActiveTab(Guid tabId)
	{
		EnsureActive();
		return window.SetActiveTab(tabId);
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
		operationError = exception.Message;
		OnPropertyChanged(nameof(StatusText));
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
		{
			return;
		}

		lifetime.Cancel();
		window.StateChanged -= Window_StateChanged;
		NavigationToolbar.Dispose();
		Toolbar.Dispose();
		commandManager.Dispose();
		foreach (var tab in tabViewModels.Values)
		{
			tab.PropertyChanged -= TabViewModel_PropertyChanged;
			tab.Dispose();
		}

		tabViewModels.Clear();
		Tabs.Clear();
		navigationSectionViewModels.Clear();
		NavigationItems.Clear();
		navigationThumbnailGate.Dispose();
		lifetime.Dispose();
	}

	private async Task LoadNavigationItemsAsync(CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var section in navigationItemLoader
				.LoadSectionsAsync(cancellationToken)
				.ConfigureAwait(false))
			{
				await ApplyNavigationSectionOnUiAsync(section)
					.ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			ReportNavigationLoadError(exception);
		}
	}

	private Task ApplyNavigationSectionOnUiAsync(NavigationSectionData section)
	{
		if (dispatcher.HasThreadAccess)
		{
			ApplyNavigationSection(section);
			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!dispatcher.TryEnqueue(
			() =>
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
		if (Volatile.Read(ref isDisposed) is not 0)
		{
			return;
		}

		if (navigationSectionViewModels.Remove(section.Order, out var previousSection))
		{
			NavigationItems.Remove(previousSection);
		}

		var children = new List<NavigationItemViewModel>(section.Items.Count);
		var navigationCancellationToken = lifetime.Token;
		foreach (var item in section.Items)
		{
			var child = NavigationItemViewModel.CreateFolder(item.Name, item.Reference);
			children.Add(child);
			_ = Task.Run(() => LoadNavigationThumbnailAsync(item, child, navigationCancellationToken));
		}

		var sectionViewModel = NavigationItemViewModel.CreateSection(section.Name, section.Reference, children);
		var insertIndex = 1;
		foreach (var order in navigationSectionViewModels.Keys)
		{
			if (order < section.Order)
			{
				insertIndex++;
			}
		}

		NavigationItems.Insert(insertIndex, sectionViewModel);
		navigationSectionViewModels.Add(section.Order, sectionViewModel);
	}

	private async Task LoadNavigationThumbnailAsync(NavigationItemData item, NavigationItemViewModel viewModel, CancellationToken cancellationToken)
	{
		try
		{
			await navigationThumbnailGate
				.WaitAsync(cancellationToken)
				.ConfigureAwait(false);
			try
			{
				var thumbnail = await navigationItemLoader
					.LoadThumbnailAsync(item.Reference, cancellationToken)
					.ConfigureAwait(false);
				if (thumbnail is null
					|| Volatile.Read(ref isDisposed) is not 0)
				{
					return;
				}

				await SetNavigationThumbnailOnUiAsync(viewModel, thumbnail)
					.ConfigureAwait(false);
			}
			finally
			{
				navigationThumbnailGate.Release();
			}
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception)
		{
			// Shell thumbnail loading is best effort.
		}
	}

	private Task SetNavigationThumbnailOnUiAsync(NavigationItemViewModel viewModel, byte[] thumbnail)
	{
		if (dispatcher.HasThreadAccess)
		{
			return SetNavigationThumbnailAsync(viewModel, thumbnail);
		}

		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!dispatcher.TryEnqueue(
			async () =>
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
		viewModel.SetThumbnail(await ThumbnailImageFactory .CreateAsync(thumbnail) .ConfigureAwait(true));
	}

	private void ReportNavigationLoadError(Exception exception)
	{
		if (Volatile.Read(ref isDisposed) is not 0)
		{
			return;
		}

		if (dispatcher.HasThreadAccess)
		{
			ReportOperationError(exception);
			return;
		}

		dispatcher.TryEnqueue(() => {if (Volatile.Read(ref isDisposed) is 0) {ReportOperationError(exception);}});
	}

	private void Window_StateChanged(object? sender, EventArgs args)
	{
		if (Interlocked.Exchange(ref refreshQueued, 1) is not 0)
		{
			return;
		}

		if (!dispatcher.TryEnqueue(() => {Interlocked.Exchange(ref refreshQueued, 0); RefreshFromCore();}))
		{
			Interlocked.Exchange(ref refreshQueued, 0);
			if (Volatile.Read(ref isDisposed) is 0)
			{
				throw new InvalidOperationException("The Files UI dispatcher rejected a window update.");
			}
		}
	}

	private void TabViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(TabViewModel.StatusText)
			or nameof(TabViewModel.ActivePane)
			or nameof(TabViewModel.Title)
			or nameof(TabViewModel.CanClosePane))
		{
			OnPropertyChanged(nameof(StatusText));
			OnPropertyChanged(nameof(ActiveFolderBrowser));
			NavigationToolbar.SetActiveFolderBrowser(ActiveFolderBrowser);
			Toolbar.SetActiveTab(ActiveTab);
			commandManager.RefreshStates();
		}
	}

	private void RefreshFromCore()
	{
		if (Volatile.Read(ref isDisposed) is not 0 || isRefreshing)
		{
			return;
		}

		isRefreshing = true;
		try
		{
			var coreTabs = window.Tabs;
			var coreTabIds = coreTabs
				.Select(static tab => tab.Id)
				.ToHashSet();

			foreach (var removedId in tabViewModels.Keys
				.Where(id => !coreTabIds.Contains(id))
				.ToArray())
			{
				var removedTab = tabViewModels[removedId];
				removedTab.PropertyChanged -= TabViewModel_PropertyChanged;
				removedTab.Dispose();
				tabViewModels.Remove(removedId);
			}

			foreach (var coreTab in coreTabs)
			{
				if (!tabViewModels.ContainsKey(coreTab.Id))
				{
					var tabViewModel = new TabViewModel(coreTab, dataRoot, dispatcher, commandManager);
					tabViewModel.PropertyChanged += TabViewModel_PropertyChanged;
					tabViewModels[coreTab.Id] = tabViewModel;
				}
			}

			var orderedTabs = coreTabs
				.Select(coreTab => tabViewModels[coreTab.Id])
				.ToArray();
			ObservableCollectionSynchronizer.Synchronize(Tabs, orderedTabs);

			var activeTabId = window.ActiveTab?.Id;
			var activeTabIndex = activeTabId is { } id
				? Tabs
					.Select((tab, index) => (tab, index))
					.FirstOrDefault(value => value.tab.Id == id)
					.index
				: -1;
			TabStrip.SetActiveTabIndex(activeTabIndex);

			operationError = null;
			OnPropertyChanged(nameof(ActiveTab));
			OnPropertyChanged(nameof(ActiveFolderBrowser));
			NavigationToolbar.SetActiveFolderBrowser(ActiveFolderBrowser);
			Toolbar.SetActiveTab(ActiveTab);
			OnPropertyChanged(nameof(StatusText));
			commandManager.RefreshStates();
		}
		finally
		{
			isRefreshing = false;
		}
	}

	private void EnsureActive() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) is not 0, this);
}
