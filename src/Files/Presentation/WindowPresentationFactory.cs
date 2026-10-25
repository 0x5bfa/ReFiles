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
using Files.Views;
using Files.Controls;
using Files.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Presentation;

internal sealed class WindowPresentationFactory
{
	private readonly Dictionary<Type, Func<IPaneContentSession, WindowCommandManager, PaneContentPresentation>> _contentPresenters = [];
	private readonly IStorageWorkspace _workspace;
	private readonly WindowsBrowseInteraction _shellInteraction;
	private readonly StorageOperationTracker _operationTracker;
	private readonly AppSettingsService _appSettings;
	private readonly IUIDispatcher _dispatcher;
	private readonly CommandRegistry _commandRegistry;
	private readonly IItemActivationService _itemActivationService;
	private readonly IItemPropertiesService? _itemPropertiesService;
	private readonly nint _ownerWindowHandle;

	internal IUIDispatcher Dispatcher => _dispatcher;

	internal AppSettingsService AppSettings => _appSettings;

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
		_shellInteraction = new WindowsBrowseInteraction(workspace, storageOperations, ownerWindowHandle);
		_operationTracker = operationTracker;
		_appSettings = appSettings;
		_dispatcher = dispatcher;
		_commandRegistry = commandRegistry;
		_itemActivationService = itemActivationService ?? Files.Activation.ItemActivationService.CreateStorageOnly();
		_itemPropertiesService = itemPropertiesService;
		_ownerWindowHandle = ownerWindowHandle;
		RegisterPaneContent<BrowsePaneSession>((content, commands) =>
		{
			var browser = CreateFolderBrowser(content, commands);

			return new PaneContentPresentation(browser, () => new FolderBrowser { ViewModel = browser }, folderBrowser: browser, preview: CreatePreviewPane(content));
		});
		RegisterPaneContent<SettingsPaneSession>((content, _) => new PaneContentPresentation(content, () => new SettingsView(_appSettings), Strings.Settings.GetLocalized(), CreateSettingsIcon()));
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

	internal void RegisterPaneContent<TContent>(Func<TContent, WindowCommandManager, PaneContentPresentation> factory) where TContent : IPaneContentSession
	{
		ArgumentNullException.ThrowIfNull(factory);

		_contentPresenters.Add(typeof(TContent), (content, commands) => factory((TContent)content, commands));
	}

	internal PaneContentPresentation CreatePaneContent(IPaneContentSession content, WindowCommandManager commands)
	{
		if (!_contentPresenters.TryGetValue(content.GetType(), out var factory))
		{
			throw new NotSupportedException($"Pane content '{content.GetType().Name}' has no registered presentation.");
		}

		return factory(content, commands);
	}


	internal FolderBrowserViewModel CreateFolderBrowser(BrowsePaneSession pane, WindowCommandManager commandManager)
	{
		return new FolderBrowserViewModel(pane, _shellInteraction, _appSettings, _dispatcher, commandManager, _ownerWindowHandle);
	}

	internal PreviewPaneViewModel CreatePreviewPane(BrowsePaneSession pane)
	{
		return new PreviewPaneViewModel(pane, _dispatcher);
	}
	private static IconSource CreateSettingsIcon()
	{
		return Application.Current?.Resources.TryGetValue("App.ThemedIcons.Settings", out var value) is true && value is ThemedIconData icon
			? new ThemedIconSource { IconData = icon, IconSize = 16 }
			: new FontIconSource { Glyph = "\uE713" };
	}

}
