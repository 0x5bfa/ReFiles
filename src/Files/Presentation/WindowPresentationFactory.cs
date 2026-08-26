// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Commands;
using Files.Activation;
using Files.Core.Data;
using Files.Core.Sessions;
using Files.Core.Storage;
using Files.Infrastructure;
using Files.ItemProperties;
using Files.Settings;
using Files.StorageOperations;
using Files.ViewModels;

namespace Files.Presentation;

internal sealed class WindowPresentationFactory
{
	private readonly IStorageWorkspace _workspace;
	private readonly IStorageOperationService _storageOperations;
	private readonly StorageOperationTracker _operationTracker;
	private readonly AppSettingsService _appSettings;
	private readonly IUIDispatcher _dispatcher;
	private readonly CommandRegistry _commandRegistry;
	private readonly IItemActivationService _itemActivationService;
	private readonly IItemPropertiesService? _itemPropertiesService;
	private readonly nint _ownerWindowHandle;

	internal IUIDispatcher Dispatcher => _dispatcher;

	internal IItemActivationService ItemActivationService => _itemActivationService;

	internal IItemPropertiesService? ItemPropertiesService => _itemPropertiesService;

	internal WindowPresentationFactory(
		IStorageWorkspace workspace,
		IStorageOperationService storageOperations,
		StorageOperationTracker operationTracker,
		AppSettingsService appSettings,
		IUIDispatcher dispatcher,
		CommandRegistry commandRegistry,
		IItemActivationService? itemActivationService = null,
		IItemPropertiesService? itemPropertiesService = null,
		nint ownerWindowHandle = 0)
	{
		ArgumentNullException.ThrowIfNull(workspace);

		ArgumentNullException.ThrowIfNull(storageOperations);

		ArgumentNullException.ThrowIfNull(operationTracker);

		ArgumentNullException.ThrowIfNull(appSettings);

		ArgumentNullException.ThrowIfNull(dispatcher);

		ArgumentNullException.ThrowIfNull(commandRegistry);

		_workspace = workspace;
		_storageOperations = storageOperations;
		_operationTracker = operationTracker;
		_appSettings = appSettings;
		_dispatcher = dispatcher;
		_commandRegistry = commandRegistry;
		_itemActivationService = itemActivationService ?? Files.Activation.ItemActivationService.CreateStorageOnly();
		_itemPropertiesService = itemPropertiesService;
		_ownerWindowHandle = ownerWindowHandle;
	}

	internal RootViewModel Create(WindowSession window)
	{
		ArgumentNullException.ThrowIfNull(window);

		return new RootViewModel(window, this);
	}

	internal WindowCommandManager CreateCommandManager(RootViewModel root)
	{
		return new WindowCommandManager(root, _commandRegistry, _dispatcher);
	}

	internal NavigationItemLoader CreateNavigationItemLoader()
	{
		return new NavigationItemLoader(_workspace);
	}

	internal StatusCenterViewModel CreateStatusCenterViewModel()
	{
		return new StatusCenterViewModel(_operationTracker, _dispatcher);
	}

	internal TabViewModel CreateTab(TabSession tab, WindowCommandManager commandManager)
	{
		return new TabViewModel(tab, this, commandManager);
	}

	internal PaneViewModel CreatePane(PaneSession pane, WindowCommandManager commandManager)
	{
		return new PaneViewModel(pane, this, commandManager);
	}

	internal FolderBrowserViewModel CreateFolderBrowser(BrowsePaneSession pane, WindowCommandManager commandManager)
	{
		return new FolderBrowserViewModel(pane, _workspace, _storageOperations, _operationTracker, _appSettings, _dispatcher, commandManager, _ownerWindowHandle);
	}

	internal PreviewPaneViewModel CreatePreviewPane(BrowsePaneSession pane)
	{
		return new PreviewPaneViewModel(pane, _dispatcher);
	}
}
