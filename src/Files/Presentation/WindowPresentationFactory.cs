// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Commands;
using Files.Adapters;
using Files.Activation;
using Files.Core.Data;
using Files.Core.Sessions;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
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
using Microsoft.Extensions.Logging;

namespace Files.Presentation;

internal sealed class WindowPresentationFactory
{
	private readonly Dictionary<Type, Func<IPaneContentSession, WindowCommandManager, PaneContentPresentation>> _contentPresenters = [];
	private readonly IStorageReader _workspace;
	private readonly IWindowsShellUIServices? _windowsServices;
	private readonly IStorageOperationService _storageOperations;
	private readonly StorageOperationTracker _operationTracker;
	private readonly AppSettingsService _appSettings;
	private readonly IUIDispatcher _dispatcher;
	private readonly CommandRegistry _commandRegistry;
	private readonly IItemActivationService _itemActivationService;
	private readonly IItemPropertiesService? _itemPropertiesService;
	private readonly nint _ownerWindowHandle;
	private readonly ILoggerFactory _loggerFactory;

	internal IUIDispatcher Dispatcher => _dispatcher;

	internal AppSettingsService AppSettings => _appSettings;

	internal IItemActivationService ItemActivationService => _itemActivationService;

	internal IItemPropertiesService? ItemPropertiesService => _itemPropertiesService;

	internal ILoggerFactory LoggerFactory => _loggerFactory;

	internal WindowPresentationFactory(
		IStorageReader workspace,
		IStorageOperationService storageOperations,
		StorageOperationTracker operationTracker,
		AppSettingsService appSettings,
		IUIDispatcher dispatcher,
		CommandRegistry commandRegistry,
		IItemActivationService? itemActivationService = null,
		IItemPropertiesService? itemPropertiesService = null,
		nint ownerWindowHandle = 0,
		ILoggerFactory? loggerFactory = null, IWindowsShellUIServices? windowsServices = null)
	{
		ArgumentNullException.ThrowIfNull(workspace);

		ArgumentNullException.ThrowIfNull(storageOperations);

		ArgumentNullException.ThrowIfNull(operationTracker);

		ArgumentNullException.ThrowIfNull(appSettings);

		ArgumentNullException.ThrowIfNull(dispatcher);

		ArgumentNullException.ThrowIfNull(commandRegistry);

		_workspace = workspace;
		_windowsServices = windowsServices;
		_storageOperations = storageOperations;
		_operationTracker = operationTracker;
		_appSettings = appSettings;
		_dispatcher = dispatcher;
		_commandRegistry = commandRegistry;
		_itemActivationService = itemActivationService ?? Files.Activation.ItemActivationService.CreateStorageOnly();
		_itemPropertiesService = itemPropertiesService;
		_ownerWindowHandle = ownerWindowHandle;
		_loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
		RegisterPaneContent<IBrowsePaneSession>((content, commands) =>
		{
			var browser = CreateFolderBrowser(content, commands);

			return new PaneContentPresentation(browser, () => new FolderBrowser { ViewModel = browser }, folderBrowser: browser, preview: CreatePreviewPane(content));
		});
		RegisterPaneContent<ISettingsPaneSession>((content, _) => new PaneContentPresentation(content, () => new SettingsView(_appSettings), Strings.Settings.GetLocalized(), CreateSettingsIcon()));
	}

	internal RootViewModel Create(IWindowSession window)
	{
		ArgumentNullException.ThrowIfNull(window);

		return new RootViewModel(window, this, _loggerFactory.CreateLogger<RootViewModel>());
	}

	internal WindowCommandManager CreateCommandManager(RootViewModel root)
	{
		return new WindowCommandManager(root, _commandRegistry, _dispatcher);
	}

	internal NavigationItemLoader CreateNavigationItemLoader()
	{
		return new NavigationItemLoader(_workspace, _loggerFactory.CreateLogger<NavigationItemLoader>(), _windowsServices);
	}

	internal StatusCenterViewModel CreateStatusCenterViewModel()
	{
		return new StatusCenterViewModel(_operationTracker, _dispatcher);
	}

	internal TabViewModel CreateTab(ITabSession tab, WindowCommandManager commandManager)
	{
		return new TabViewModel(tab, this, commandManager);
	}

	internal PaneViewModel CreatePane(IPaneSession pane, WindowCommandManager commandManager)
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
			factory = _contentPresenters.FirstOrDefault(pair => pair.Key.IsInstanceOfType(content)).Value;
			if (factory is null)
			{
				throw new NotSupportedException($"Pane content '{content.GetType().Name}' has no registered presentation.");
			}
		}

		return factory(content, commands);
	}

	internal FolderBrowserViewModel CreateFolderBrowser(IBrowsePaneSession pane, WindowCommandManager commandManager)
	{
		var adapter = new BrowsePresentationAdapter(pane, _dispatcher, ownerWindowHandle: _ownerWindowHandle, loggerFactory: _loggerFactory);
		var interaction = new WindowsBrowseInteraction(pane, adapter, _storageOperations, _dispatcher, commandManager, _ownerWindowHandle,
			_loggerFactory.CreateLogger<WindowsBrowseInteraction>(), _windowsServices);

		return new FolderBrowserViewModel(pane, adapter, interaction, _appSettings, _dispatcher, commandManager, _ownerWindowHandle, _loggerFactory);
	}

	internal PreviewPaneViewModel CreatePreviewPane(IBrowsePaneSession pane)
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
