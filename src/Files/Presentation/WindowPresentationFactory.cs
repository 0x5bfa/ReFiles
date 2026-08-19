// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Commands;
using Files.Activation;
using Files.Core.Data;
using Files.Core.Sessions;
using Files.Core.Storage;
using Files.Infrastructure;
using Files.ViewModels;

namespace Files.Presentation;

internal sealed class WindowPresentationFactory
{
	private readonly IStorageWorkspace _workspace;
	private readonly IStorageOperationService _storageOperations;
	private readonly IUIDispatcher _dispatcher;
	private readonly CommandRegistry _commandRegistry;
	private readonly IItemActivationService _itemActivationService;

	internal IUIDispatcher Dispatcher => _dispatcher;

	internal IItemActivationService ItemActivationService => _itemActivationService;

	internal WindowPresentationFactory(IStorageWorkspace workspace, IStorageOperationService storageOperations, IUIDispatcher dispatcher, CommandRegistry commandRegistry, IItemActivationService? itemActivationService = null)
	{
		ArgumentNullException.ThrowIfNull(workspace);
		ArgumentNullException.ThrowIfNull(storageOperations);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(commandRegistry);

		_workspace = workspace;
		_storageOperations = storageOperations;
		_dispatcher = dispatcher;
		_commandRegistry = commandRegistry;
		_itemActivationService = itemActivationService ?? Files.Activation.ItemActivationService.CreateStorageOnly();
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
		return new FolderBrowserViewModel(pane, _workspace, _storageOperations, _dispatcher, commandManager);
	}

	internal PreviewPaneViewModel CreatePreviewPane(BrowsePaneSession pane)
	{
		return new PreviewPaneViewModel(pane, _dispatcher);
	}
}
