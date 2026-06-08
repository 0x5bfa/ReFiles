// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
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
	private readonly Dictionary<Guid, TabViewModel> tabViewModels = [];
	private string? operationError;
	private int isDisposed;
	private int refreshQueued;
	private bool isRefreshing;

	public RootViewModel(
		WindowModel window,
		IFilesDataRoot dataRoot,
		IUIDispatcher dispatcher,
		CommandRegistry commandRegistry)
	{
		ArgumentNullException.ThrowIfNull(window);
		ArgumentNullException.ThrowIfNull(dataRoot);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(commandRegistry);

		this.window = window;
		this.dataRoot = dataRoot;
		this.dispatcher = dispatcher;
		Tabs = [];
		commandManager = new WindowCommandManager(
			this,
			commandRegistry,
			dispatcher);
		TabStrip = new(
			Tabs,
			NewTabCommand,
			CloseTabCommand,
			SetActiveTabAt);
		NavigationToolbar = new(
			BackCommand,
			ForwardCommand,
			UpCommand,
			HomeCommand,
			NavigatePathCommand,
			RefreshCommand);
		Toolbar = new(NewPaneCommand, ClosePaneCommand);

		window.StateChanged += Window_StateChanged;
		RefreshFromCore();
	}

	public ObservableCollection<TabViewModel> Tabs { get; }

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

	public async Task InitializeAsync()
	{
		EnsureActive();
		if (ActiveTab?.ActivePane is { } pane)
		{
			await pane.FolderBrowser.InitializeAsync().ConfigureAwait(false);
		}
	}

	public async Task OpenTabAsync(
		CancellationToken cancellationToken = default)
	{
		EnsureActive();
		await window.OpenTabAsync(
				HomeLocation.Instance,
				cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task CloseTabAsync(
		Guid tabId,
		CancellationToken cancellationToken = default)
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
	}

	private void Window_StateChanged(object? sender, EventArgs args)
	{
		if (Interlocked.Exchange(ref refreshQueued, 1) is not 0)
		{
			return;
		}

		if (!dispatcher.TryEnqueue(
			() =>
			{
				Interlocked.Exchange(ref refreshQueued, 0);
				RefreshFromCore();
			}))
		{
			Interlocked.Exchange(ref refreshQueued, 0);
			if (Volatile.Read(ref isDisposed) is 0)
			{
				throw new InvalidOperationException(
					"The Files UI dispatcher rejected a window update.");
			}
		}
	}

	private void TabViewModel_PropertyChanged(
		object? sender,
		PropertyChangedEventArgs e)
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
					var tabViewModel = new TabViewModel(
						coreTab,
						dataRoot,
						dispatcher,
						commandManager);
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
		ObjectDisposedException.ThrowIf(
			Volatile.Read(ref isDisposed) is not 0,
			this);
}
