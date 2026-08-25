// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.Adapters;
using Files.Commands;
using Files.Infrastructure;
using Files.Localization;
using Files.Settings;
using Files.Core.Sessions;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Windows;
using Files.Core.ViewSettings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using OwlCore.Storage;
using Windows.Foundation;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Files.ViewModels;

public enum FolderViewMode
{
	Details,
	List,
	Cards,
	Grid,
	Columns,
}

public sealed class FolderBrowserViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
	private const int BulkNotificationThreshold = 32;
	private const int BreadcrumbThumbnailSize = 16;
	private const int LocationIconSize = 16;

	private readonly BrowsePresentationAdapter _browseAdapter;
	private readonly BrowsePaneSession _pane;
	private readonly IStorageWorkspace _workspace;
	private readonly IStorageOperationService _storageOperations;
	private readonly AppSettingsService _appSettings;
	private readonly WindowsStorageSource? _windowsSource;
	private readonly WindowsShellNewMenu? _shellNewMenu;
	private readonly WindowsShellAppExtensionService? _shellAppExtensionService;
	private readonly nint _ownerWindowHandle;

	private readonly IUIDispatcher _dispatcher;

	private readonly CancellationTokenSource _lifetime = new();

	private CollectionViewSource _itemsViewSource;

	private string? _operationError;
	private BitmapImage? _locationIcon;
	private CancellationTokenSource? _locationIconCancellation;
	private CancellationTokenSource? _displaySettingsCancellation;

	private bool _isApplyingUpdate;

	private bool _wasLoading;
	private bool _wasBusy;

	private int _isDisposed;
	private int _groupRefreshQueued;

	private FolderViewMode _viewMode = FolderViewMode.Details;

	internal WindowCommandManager CommandManager { get; }

	internal IUIDispatcher Dispatcher => _dispatcher;

	internal bool CanShowShellContextMenu => _ownerWindowHandle is not 0 && _shellAppExtensionService is not null;

	public BulkObservableCollection<BrowseItemViewModel> Items { get; } = [];

	public BulkObservableCollection<BrowseItemGroupViewModel> ItemGroups { get; } = [];

	public CollectionViewSource ItemsViewSource => _itemsViewSource;

	public IReadOnlyList<DetailsColumnViewModel> DetailsColumns => _browseAdapter.DetailsColumns;

	public BrowseViewSettings ViewSettings => _browseAdapter.ViewSettings;

	public bool ShowHiddenItems => _appSettings.ShowHiddenItems;

	public bool ShowFileExtensions => _appSettings.ShowFileExtensions;

	public double LayoutSize => Math.Clamp(Math.Round(ViewSettings.ItemSize ?? 3), 1, 5);

	public double DetailsRowHeight => 28 + ((LayoutSize - 1) * 8);

	public double ListThumbnailSize => 24 + ((LayoutSize - 1) * 8);

	public double ListItemHeight => ListThumbnailSize + 12;

	public double CardsThumbnailSize => 48 + ((LayoutSize - 1) * 12);

	public double CardsItemHeight => CardsThumbnailSize + 24;

	public double GridItemSize => 104 + ((LayoutSize - 1) * 28);

	public double GridThumbnailSize => GridItemSize - 44;

	public double GridDefaultIconSize => GridThumbnailSize * 0.57;

	public FolderViewMode ViewMode
	{
		get => _viewMode;
		private set => SetProperty(ref _viewMode, value);
	}

	public bool IsApplyingUpdate => _isApplyingUpdate;

	public IReadOnlyList<StorableKey> SelectedKeys => _browseAdapter.SelectedKeys;

	public IReadOnlyList<BrowseItemViewModel> SelectedItems
	{
		get => _browseAdapter.GetItems(SelectedKeys);
	}

	public bool CanCopy => !IsLoading && !IsBusy && GetSelectedFilePaths().Count is not 0;

	public bool CanCut => CanCopy;

	public bool CanPaste => !IsLoading && !IsBusy && TryGetCurrentFileSystemFolder(out _) && FileClipboard.HasStorageItems;

	public bool CanDelete => !IsLoading && !IsBusy && SelectedItems.Count is not 0 && SelectedItems.All(item => _storageOperations.CanHandle(CreateDeleteRequest(item.Reference)));

	public bool CanShowNew => !IsLoading && !IsBusy && _shellNewMenu is not null && TryGetCurrentFileSystemFolder(out _);

	public string LocationText => _browseAdapter.LocationText;

	public BrowseLocation? Location => _pane.Location;

	public string LocationDisplayName => _pane.BrowseSession.Context?.LocationModel?.Name ?? LocationText;

	public BitmapImage? LocationIcon => _locationIcon;

	public bool IsLoading => _browseAdapter.IsLoading;

	public bool IsFolderEmpty => !IsLoading && Location is not null && Items.Count is 0 && _operationError is null && _browseAdapter.ErrorMessage is null;

	public bool IsBusy => _browseAdapter.IsBusy;

	public bool CanGoBack => _browseAdapter.CanGoBack;

	public bool CanGoForward => _browseAdapter.CanGoForward;

	public bool CanGoUp => _browseAdapter.CanGoUp;

	public bool CanRefresh => !IsLoading;

	public string StatusText =>
		_operationError
		?? _browseAdapter.ErrorMessage
		?? _browseAdapter.StatusText;

	public FolderBrowserViewModel(BrowsePaneSession pane, IStorageWorkspace workspace, IStorageOperationService storageOperations, IUIDispatcher dispatcher, WindowCommandManager commandManager,
		nint ownerWindowHandle = 0)
	{
		ArgumentNullException.ThrowIfNull(pane);

		ArgumentNullException.ThrowIfNull(workspace);

		ArgumentNullException.ThrowIfNull(storageOperations);

		ArgumentNullException.ThrowIfNull(commandManager);

		ArgumentNullException.ThrowIfNull(dispatcher);

		CommandManager = commandManager;
		_pane = pane;
		_workspace = workspace;
		_storageOperations = storageOperations;
		_dispatcher = dispatcher;
		_appSettings = ((App)Microsoft.UI.Xaml.Application.Current).Settings;
		_browseAdapter = new BrowsePresentationAdapter(pane, workspace, dispatcher);
		_windowsSource = workspace.Sources.OfType<WindowsStorageSource>().FirstOrDefault();
		_shellNewMenu = _windowsSource is { } windowsSource
			? new WindowsShellNewMenu(windowsSource.Scheduler)
			: null;
		_shellAppExtensionService = _windowsSource is { } shellSource ? new WindowsShellAppExtensionService(shellSource) : null;
		_ownerWindowHandle = ownerWindowHandle;
		_itemsViewSource = CreateItemsViewSource(Items, isGrouped: false);
		_viewMode = ToFolderViewMode(_browseAdapter.LayoutMode);
		_wasLoading = _browseAdapter.IsLoading;
		_wasBusy = _browseAdapter.IsBusy;
		_browseAdapter.Updated += BrowseAdapter_Updated;
		_appSettings.PropertyChanged += AppSettings_PropertyChanged;
		RefreshLocationIcon();
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		await ApplyDisplaySettingsAsync(cancellationToken).ConfigureAwait(false);
		await _browseAdapter.InitializeAsync(cancellationToken).ConfigureAwait(false);
	}

	public Task NavigateToPathAsync(string path, CancellationToken cancellationToken = default) =>
		_browseAdapter.NavigateToPathAsync(path, cancellationToken);

	public Task NavigateHomeAsync(CancellationToken cancellationToken = default) =>
		_browseAdapter.NavigateHomeAsync(cancellationToken);

	public Task NavigateToItemAsync(BrowseItemViewModel item, CancellationToken cancellationToken = default) =>
		_browseAdapter.NavigateToItemAsync(item, cancellationToken);

	public Task NavigateToReferenceAsync(StorableReference reference, CancellationToken cancellationToken = default) =>
		_browseAdapter.NavigateToReferenceAsync(reference, cancellationToken);

	public Task GoBackAsync(CancellationToken cancellationToken = default) =>
		_browseAdapter.GoBackAsync(cancellationToken);

	public Task GoForwardAsync(CancellationToken cancellationToken = default) =>
		_browseAdapter.GoForwardAsync(cancellationToken);

	public Task GoUpAsync(CancellationToken cancellationToken = default) =>
		_browseAdapter.GoUpAsync(cancellationToken);

	public Task RefreshAsync(CancellationToken cancellationToken = default) =>
		_browseAdapter.RefreshAsync(cancellationToken);

	internal Task NavigateToLocationAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		return _browseAdapter.NavigateToLocationAsync(location, cancellationToken);
	}

	internal async Task<IReadOnlyList<NavigationToolbarBreadcrumbItem>> GetBreadcrumbItemsAsync(CancellationToken cancellationToken = default)
	{
		return Location switch
		{
			HomeLocation => [],
			FolderLocation folderLocation => await CreateFolderBreadcrumbItemsAsync(folderLocation, cancellationToken).ConfigureAwait(false),
			ArchiveLocation archiveLocation => await CreateArchiveBreadcrumbItemsAsync(archiveLocation, cancellationToken).ConfigureAwait(false),
			{ } location => [new NavigationToolbarBreadcrumbItem(LocationDisplayName, location, false)],
			null => [],
		};
	}

	internal Task<IReadOnlyList<NavigationToolbarBreadcrumbItem>> GetBreadcrumbChildrenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		return location switch
		{
			HomeLocation => CreateHomeBreadcrumbChildrenAsync(cancellationToken),
			FolderLocation folderLocation => CreateFolderBreadcrumbChildrenAsync(folderLocation, cancellationToken),
			ArchiveLocation archiveLocation => CreateArchiveBreadcrumbChildrenAsync(archiveLocation, cancellationToken),
			_ => Task.FromResult<IReadOnlyList<NavigationToolbarBreadcrumbItem>>([]),
		};
	}

	public void UpdateViewport(BrowseViewport viewport) =>
		_browseAdapter.UpdateViewport(viewport);

	public void SetSelection(IEnumerable<BrowseItemViewModel> selectedItems) =>
		SetSelectionCore(selectedItems);

	public async Task CopySelectionAsync(bool move, CancellationToken cancellationToken = default)
	{
		var paths = GetSelectedFilePaths();
		if (paths.Count is 0)
		{
			throw new NotSupportedException("Only local file-system items can be copied to the Windows clipboard.");
		}

		await FileClipboard.SetStorageItemsAsync(paths, move, cancellationToken).ConfigureAwait(false);
	}

	public async Task PasteFromClipboardAsync(CancellationToken cancellationToken = default)
	{
		if (!TryGetCurrentFileSystemFolder(out var destinationFolder))
		{
			throw new NotSupportedException("The current location cannot receive file-system clipboard items.");
		}

		var content = await FileClipboard.GetStorageItemsAsync(cancellationToken).ConfigureAwait(false);
		if (content is null)
		{
			return;
		}

		var move = content.RequestedOperation.HasFlag(Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move);
		foreach (var path in content.Paths)
		{
			await using var model = await _workspace.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, path), cancellationToken).ConfigureAwait(false);
			StorageOperationRequest request = move
				? new MoveOperationRequest(model.Reference, destinationFolder, conflictBehavior: StorageConflictBehavior.GenerateUniqueName)
				: new CopyOperationRequest(model.Reference, destinationFolder, conflictBehavior: StorageConflictBehavior.GenerateUniqueName);

			await ExecuteStorageOperationAsync(request, cancellationToken).ConfigureAwait(false);
		}

		if (move)
		{
			Windows.ApplicationModel.DataTransfer.Clipboard.Clear();
		}

		await RefreshAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task DeleteSelectionAsync(CancellationToken cancellationToken = default)
	{
		var selectedItems = SelectedItems;
		if (selectedItems.Count is 0)
		{
			return;
		}

		foreach (var item in selectedItems)
		{
			await ExecuteStorageOperationAsync(CreateDeleteRequest(item.Reference), cancellationToken).ConfigureAwait(false);
		}

		await RefreshAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<WindowsShellNewItem>> GetNewItemsAsync(CancellationToken cancellationToken = default)
	{
		if (_shellNewMenu is null || !TryGetCurrentFileSystemFolder(out var folder))
		{
			return [];
		}

		var path = folder.LastKnownAddress!.Value;

		return await _shellNewMenu.GetItemsAsync(path, cancellationToken).ConfigureAwait(false);
	}

	public async Task InvokeNewItemAsync(WindowsShellNewItem item, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(item);

		if (_shellNewMenu is null || !TryGetCurrentFileSystemFolder(out var folder))
		{
			throw new NotSupportedException("The current location does not expose a Windows Shell New menu.");
		}

		var path = folder.LastKnownAddress!.Value;
		if (!await _shellNewMenu.InvokeAsync(path, item.CommandOffset, cancellationToken).ConfigureAwait(false))
		{
			throw new InvalidOperationException($"The Windows Shell could not invoke the New menu item '{item.Name}'.");
		}

		await RefreshAsync(cancellationToken).ConfigureAwait(false);
	}

	internal Task<IReadOnlyList<WindowsShellAppExtensionCommand>> GetAppExtensionCommandsAsync(IReadOnlyList<BrowseItemViewModel> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		return _shellAppExtensionService is null
			? Task.FromResult<IReadOnlyList<WindowsShellAppExtensionCommand>>([])
			: _shellAppExtensionService.GetCommandsAsync(selection.Select(static item => item.Reference).ToArray(), cancellationToken);
	}

	internal Task<ReadOnlyMemory<byte>> GetAppExtensionIconAsync(WindowsShellAppExtensionCommand command, int pixelSize, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		return _shellAppExtensionService is null ? Task.FromResult(ReadOnlyMemory<byte>.Empty) : _shellAppExtensionService.GetCommandIconAsync(command, pixelSize, cancellationToken);
	}

	internal Task<bool> InvokeAppExtensionCommandAsync(IReadOnlyList<BrowseItemViewModel> selection, WindowsShellAppExtensionCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);
		ArgumentNullException.ThrowIfNull(command);

		return _shellAppExtensionService is null ? Task.FromResult(false) : _shellAppExtensionService.InvokeAsync(selection.Select(static item => item.Reference).ToArray(), command, cancellationToken);
	}

	internal Task<bool> ShowShellPropertiesAsync(IReadOnlyList<BrowseItemViewModel> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		return _shellAppExtensionService is null ? Task.FromResult(false) : _shellAppExtensionService.ShowShellPropertiesAsync(selection.Select(static item => item.Reference).ToArray(), cancellationToken);
	}

	internal Task<WindowsShellContextMenuTarget?> GetShellContextMenuTargetAsync(IReadOnlyList<BrowseItemViewModel> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		return _shellAppExtensionService is null
			? Task.FromResult<WindowsShellContextMenuTarget?>(null)
			: _shellAppExtensionService.GetContextMenuTargetAsync(selection.Select(static item => item.Reference).ToArray(), cancellationToken);
	}

	internal bool ShowShellContextMenu(WindowsShellContextMenuTarget target, Point clientPoint, double rasterizationScale)
	{
		ArgumentNullException.ThrowIfNull(target);

		if (_ownerWindowHandle is 0)
		{
			return false;
		}

		var point = new System.Drawing.Point(checked((int)Math.Round(clientPoint.X * rasterizationScale)), checked((int)Math.Round(clientPoint.Y * rasterizationScale)));
		var owner = new HWND(_ownerWindowHandle);
		if (PInvoke.ClientToScreen(owner, ref point).Value is 0)
		{
			return false;
		}

		return new WindowsShellContextMenuSession().Show(owner, target, point);
	}

	public async Task SetViewModeAsync(FolderViewMode mode, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		if (!Enum.IsDefined(mode))
		{
			throw new ArgumentOutOfRangeException(nameof(mode));
		}

		await SetViewModeOnUiAsync(mode).ConfigureAwait(false);
		try
		{
			await _browseAdapter.UpdateLayoutModeAsync(ToViewLayoutMode(mode), cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			await SetViewModeOnUiAsync(ToFolderViewMode(_browseAdapter.LayoutMode)).ConfigureAwait(false);
			throw;
		}
	}

	public ValueTask SetSortAsync(string propertyId, ViewSortDirection direction, CancellationToken cancellationToken = default)
	{
		return _browseAdapter.UpdateSortAsync(propertyId, direction, cancellationToken);
	}

	public ValueTask SetGroupingAsync(string? propertyId, ViewSortDirection direction, CancellationToken cancellationToken = default)
	{
		return _browseAdapter.UpdateGroupingAsync(propertyId, direction, cancellationToken);
	}

	public ValueTask SetColumnsAsync(IEnumerable<ViewColumnSettings> columns, CancellationToken cancellationToken = default)
	{
		return _browseAdapter.UpdateColumnsAsync(columns, cancellationToken);
	}

	public ValueTask SetItemSizeAsync(double itemSize, CancellationToken cancellationToken = default)
	{
		return _browseAdapter.UpdateItemSizeAsync(Math.Clamp(Math.Round(itemSize), 1, 5), cancellationToken);
	}

	public ValueTask SetShowHiddenItemsAsync(bool showHiddenItems, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		_appSettings.ShowHiddenItems = showHiddenItems;

		return ValueTask.CompletedTask;
	}

	public ValueTask SetShowFileExtensionsAsync(bool showFileExtensions, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		_appSettings.ShowFileExtensions = showFileExtensions;

		return ValueTask.CompletedTask;
	}

	public void ReportOperationError(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);

		_operationError = exception.Message;
		OnPropertyChanged(nameof(StatusText));
		OnPropertyChanged(nameof(IsFolderEmpty));
	}

	public void ReportOperationCanceled()
	{
		_operationError = Strings.OperationCanceled.GetLocalized();
		OnPropertyChanged(nameof(StatusText));
		OnPropertyChanged(nameof(IsFolderEmpty));
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

		_browseAdapter.Updated -= BrowseAdapter_Updated;
		_appSettings.PropertyChanged -= AppSettings_PropertyChanged;
		Interlocked.Exchange(ref _locationIconCancellation, null)?.Cancel();
		Interlocked.Exchange(ref _displaySettingsCancellation, null)?.Cancel();
		_lifetime.Cancel();
		try
		{
			await _browseAdapter.DisposeAsync().ConfigureAwait(false);
		}
		finally
		{
			_lifetime.Dispose();
		}
	}

	private async void AppSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName is not nameof(AppSettingsService.ShowHiddenItems) and not nameof(AppSettingsService.ShowFileExtensions))
		{
			return;
		}

		if (!_dispatcher.HasThreadAccess)
		{
			if (Volatile.Read(ref _isDisposed) is 0)
			{
				_dispatcher.TryEnqueue(() => AppSettings_PropertyChanged(sender, e));
			}

			return;
		}

		OnPropertyChanged(nameof(ShowHiddenItems));
		OnPropertyChanged(nameof(ShowFileExtensions));
		CommandManager.RefreshStates(CommandStateInvalidation.ViewSettings);
		var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		Interlocked.Exchange(ref _displaySettingsCancellation, cancellation)?.Cancel();
		try
		{
			await ApplyDisplaySettingsAsync(cancellation.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch (ObjectDisposedException) when (Volatile.Read(ref _isDisposed) is not 0)
		{
		}
		catch (Exception exception)
		{
			_dispatcher.TryEnqueue(() => ReportOperationError(exception));
		}
		finally
		{
			Interlocked.CompareExchange(ref _displaySettingsCancellation, null, cancellation);
			cancellation.Dispose();
		}
	}

	private Task ApplyDisplaySettingsAsync(CancellationToken cancellationToken)
	{
		var settings = new BrowseDisplaySettings(_appSettings.ShowHiddenItems, _appSettings.ShowFileExtensions);

		return _browseAdapter.UpdateDisplaySettingsAsync(settings, cancellationToken).AsTask();
	}

	private async Task<IReadOnlyList<NavigationToolbarBreadcrumbItem>> CreateFolderBreadcrumbItemsAsync(FolderLocation location, CancellationToken cancellationToken)
	{
		if (_pane.BrowseSession.Context is not { Location: FolderLocation contextLocation, LocationModel: IFolderModel currentFolder } || !Equals(contextLocation, location))
		{
			return [new NavigationToolbarBreadcrumbItem(LocationDisplayName, location, false)];
		}

		var reversedItems = new List<(string Text, FolderLocation Location)>();
		var visitedLocations = new HashSet<StorableReference>();
		IFolderModel? model = currentFolder;
		IFolderModel? ownedModel = null;
		try
		{
			while (model is not null)
			{
				if (!visitedLocations.Add(model.Reference))
				{
					break;
				}

				reversedItems.Add((model.Name, new FolderLocation(model.Reference)));
				var parent = await model.GetParentAsync(cancellationToken).ConfigureAwait(false);
				if (ownedModel is not null)
				{
					await ownedModel.DisposeAsync().ConfigureAwait(false);
				}

				ownedModel = parent;
				model = parent;
			}
		}
		finally
		{
			if (ownedModel is not null)
			{
				await ownedModel.DisposeAsync().ConfigureAwait(false);
			}
		}

		reversedItems.Reverse();
		if (reversedItems.Count > 0 && await IsShellDesktopLocationAsync(reversedItems[0].Location, cancellationToken).ConfigureAwait(false))
		{
			reversedItems.RemoveAt(0);
		}

		var leafHasChildren = await HasFolderChildrenAsync(currentFolder, cancellationToken).ConfigureAwait(false);
		var items = new List<NavigationToolbarBreadcrumbItem>(reversedItems.Count);
		for (var index = 0; index < reversedItems.Count; index++)
		{
			var item = reversedItems[index];
			items.Add(new NavigationToolbarBreadcrumbItem(item.Text, item.Location, index < reversedItems.Count - 1 || leafHasChildren));
		}

		return items;
	}

	private async Task<IReadOnlyList<NavigationToolbarBreadcrumbItem>> CreateArchiveBreadcrumbItemsAsync(ArchiveLocation location, CancellationToken cancellationToken)
	{
		await using var archiveModel = await _workspace.ResolveAsync(location.Archive, cancellationToken).ConfigureAwait(false);
		var context = _pane.BrowseSession.Context;
		var leafHasChildren = context is { Location: ArchiveLocation contextLocation, LocationModel: IFolderModel currentFolder }
			&& Equals(contextLocation, location)
			&& await HasFolderChildrenAsync(currentFolder, cancellationToken).ConfigureAwait(false);
		var segments = string.IsNullOrEmpty(location.EntryPath) ? [] : location.EntryPath.Split('/');
		var items = new List<NavigationToolbarBreadcrumbItem>(segments.Length + 1)
		{
			new(archiveModel.Name, new ArchiveLocation(location.Archive), segments.Length > 0 || leafHasChildren),
		};
		var entryPath = string.Empty;
		for (var index = 0; index < segments.Length; index++)
		{
			entryPath = ArchiveEntryPath.Combine(entryPath, segments[index]);
			items.Add(new NavigationToolbarBreadcrumbItem(segments[index], new ArchiveLocation(location.Archive, entryPath), index < segments.Length - 1 || leafHasChildren));
		}

		return items;
	}

	private async Task<IReadOnlyList<NavigationToolbarBreadcrumbItem>> CreateHomeBreadcrumbChildrenAsync(CancellationToken cancellationToken)
	{
		var items = new List<NavigationToolbarBreadcrumbItem>();
		foreach (var source in _workspace.Sources)
		{
			await foreach (var root in _workspace.GetRootsAsync(source.SourceId, cancellationToken).ConfigureAwait(false))
			{
				try
				{
					if (!_appSettings.ShowHiddenItems && root.IsHidden)
					{
						continue;
					}

					var thumbnailData = await LoadBreadcrumbThumbnailAsync(root, cancellationToken).ConfigureAwait(false);
					items.Add(new NavigationToolbarBreadcrumbItem(root.Name, new FolderLocation(root.Reference), false, thumbnailData));
				}
				finally
				{
					await root.DisposeAsync().ConfigureAwait(false);
				}
			}
		}

		return items;
	}

	private async Task<IReadOnlyList<NavigationToolbarBreadcrumbItem>> CreateFolderBreadcrumbChildrenAsync(FolderLocation location, CancellationToken cancellationToken)
	{
		var items = new List<NavigationToolbarBreadcrumbItem>();
		await using var model = await _workspace.ResolveAsync(location.Folder, cancellationToken).ConfigureAwait(false);
		if (model is not IFolderModel folder)
		{
			return items;
		}

		await foreach (var child in folder.GetItemsAsync(StorableType.Folder, cancellationToken).ConfigureAwait(false))
		{
			try
			{
				if (!_appSettings.ShowHiddenItems && child.IsHidden)
				{
					continue;
				}

				var thumbnailData = await LoadBreadcrumbThumbnailAsync(child, cancellationToken).ConfigureAwait(false);
				items.Add(new NavigationToolbarBreadcrumbItem(child.Name, new FolderLocation(child.Reference), false, thumbnailData));
			}
			finally
			{
				await child.DisposeAsync().ConfigureAwait(false);
			}
		}

		return items;
	}

	private async Task<IReadOnlyList<NavigationToolbarBreadcrumbItem>> CreateArchiveBreadcrumbChildrenAsync(ArchiveLocation location, CancellationToken cancellationToken)
	{
		if (_pane.BrowseSession.Context is not ArchiveBrowseLocationContext context || context.Location is not ArchiveLocation contextLocation || !Equals(contextLocation.Archive, location.Archive))
		{
			return [];
		}

		var items = new List<NavigationToolbarBreadcrumbItem>();
		await foreach (var child in context.GetItemsAsync(location, StorableType.Folder, cancellationToken).ConfigureAwait(false))
		{
			try
			{
				if (!_appSettings.ShowHiddenItems && child.IsHidden)
				{
					continue;
				}

				var entryPath = ArchiveEntryPath.Combine(location.EntryPath, child.Name);
				var thumbnailData = await LoadBreadcrumbThumbnailAsync(child, cancellationToken).ConfigureAwait(false);
				items.Add(new NavigationToolbarBreadcrumbItem(child.Name, new ArchiveLocation(location.Archive, entryPath), false, thumbnailData));
			}
			finally
			{
				await child.DisposeAsync().ConfigureAwait(false);
			}
		}

		return items;
	}

	private async Task<bool> IsShellDesktopLocationAsync(FolderLocation location, CancellationToken cancellationToken)
	{
		return _windowsSource is not null && await _windowsSource.IsShellDesktopAsync(location.Folder, cancellationToken).ConfigureAwait(false);
	}

	private static async Task<ReadOnlyMemory<byte>> LoadBreadcrumbThumbnailAsync(IStorableModel model, CancellationToken cancellationToken)
	{
		if (model.Get<IThumbnailSource>() is not { } source)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		try
		{
			var result = await source.GetThumbnailAsync(new ThumbnailRequest(BreadcrumbThumbnailSize, ThumbnailMode.Icon), cancellationToken).ConfigureAwait(false);

			return result is null ? ReadOnlyMemory<byte>.Empty : result.Content.ToArray();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception error)
		{
			UiDiagnosticLog.Write("NavigationToolbar", $"Breadcrumb thumbnail failed for '{model.Reference.ItemId}': {error.GetType().Name}");

			return ReadOnlyMemory<byte>.Empty;
		}
	}

	private async Task<bool> HasFolderChildrenAsync(IFolderModel folder, CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var child in folder.GetItemsAsync(StorableType.Folder, cancellationToken).ConfigureAwait(false))
			{
				try
				{
					if (!_appSettings.ShowHiddenItems && child.IsHidden)
					{
						continue;
					}

					return true;
				}
				finally
				{
					await child.DisposeAsync().ConfigureAwait(false);
				}
			}
		}
		catch (Exception) when (!cancellationToken.IsCancellationRequested)
		{
		}

		return false;
	}

	private void BrowseAdapter_Updated(object? sender, CoreBrowseUpdatedEventArgs args)
	{
		var updateStartTimestamp = Stopwatch.GetTimestamp();
		var itemCountBefore = Items.Count;
		var wasLoading = _wasLoading;
		var wasBusy = _wasBusy;
		var hadOperationError = _operationError is not null;
		_wasLoading = _browseAdapter.IsLoading;
		_wasBusy = _browseAdapter.IsBusy;
		_isApplyingUpdate = true;
		var refreshItemsViewSource = false;
		var selectionSynchronized = false;
		try
		{
			if (args.Flags.HasFlag(BrowseUpdateFlags.ViewSettings))
			{
				ViewMode = ToFolderViewMode(_browseAdapter.LayoutMode);
				OnPropertyChanged(nameof(ViewSettings));
				OnPropertyChanged(nameof(LayoutSize));
				OnPropertyChanged(nameof(DetailsRowHeight));
				OnPropertyChanged(nameof(ListThumbnailSize));
				OnPropertyChanged(nameof(ListItemHeight));
				OnPropertyChanged(nameof(CardsThumbnailSize));
				OnPropertyChanged(nameof(CardsItemHeight));
				OnPropertyChanged(nameof(GridItemSize));
				OnPropertyChanged(nameof(GridThumbnailSize));
				OnPropertyChanged(nameof(GridDefaultIconSize));
				refreshItemsViewSource = true;
			}

			if (args.Flags.HasFlag(BrowseUpdateFlags.Items) && args.ItemChanges.Count is not 0)
			{
				var shouldReplaceItems = ShouldReplaceItems(args.ItemChanges, wasLoading, _browseAdapter.IsLoading);
				UiDiagnosticLog.Write(
					"FolderBrowserViewModel",
					$"Applying changes={args.ItemChanges.Count} replace={shouldReplaceItems} before={itemCountBefore} loadingBefore={wasLoading} loadingAfter={_browseAdapter.IsLoading}");
				if (shouldReplaceItems)
				{
					var replaceStartTimestamp = Stopwatch.GetTimestamp();
					Items.ReplaceAll(_browseAdapter.Items);
					UiDiagnosticLog.Write("FolderBrowserViewModel", $"ReplaceAll completed items={Items.Count} elapsedMs={Stopwatch.GetElapsedTime(replaceStartTimestamp).TotalMilliseconds:F1}");
				}
				else if (CanApplyItemChanges(args.ItemChanges))
				{
					ApplyItemChanges(args.ItemChanges);
				}
				else
				{
					Items.ReplaceAll(_browseAdapter.Items);
				}

				refreshItemsViewSource = true;
			}

			if (refreshItemsViewSource)
			{
				var deferGrouping = ViewSettings.GroupPropertyId is not null
					&& _browseAdapter.IsLoading
					&& args.Flags.HasFlag(BrowseUpdateFlags.Items)
					&& !args.Flags.HasFlag(BrowseUpdateFlags.ViewSettings);
				if (deferGrouping)
				{
					QueueGroupRefresh();
				}
				else
				{
					RefreshItemsViewSource();
				}
			}
			else if (args.Flags.HasFlag(BrowseUpdateFlags.Presentation))
			{
				QueueGroupRefresh();
			}

			if (args.Flags is not BrowseUpdateFlags.None)
			{
				_operationError = null;
			}

			if (args.Flags.HasFlag(BrowseUpdateFlags.Columns))
			{
				OnPropertyChanged(nameof(DetailsColumns));
			}

			selectionSynchronized = ShouldSynchronizeSelection(args);
			if (selectionSynchronized)
			{
				OnPropertyChanged(nameof(SelectedKeys));
			}

			if (args.Flags.HasFlag(BrowseUpdateFlags.Location))
			{
				OnPropertyChanged(nameof(Location));
				OnPropertyChanged(nameof(LocationText));
				OnPropertyChanged(nameof(LocationDisplayName));
				OnPropertyChanged(nameof(CanShowNew));
				RefreshLocationIcon();
			}

			if (args.Flags.HasFlag(BrowseUpdateFlags.Loading))
			{
				OnPropertyChanged(nameof(IsLoading));
				OnPropertyChanged(nameof(IsBusy));
				OnPropertyChanged(nameof(CanRefresh));
				OnPropertyChanged(nameof(CanShowNew));
			}
			else if (wasBusy != _browseAdapter.IsBusy)
			{
				OnPropertyChanged(nameof(IsBusy));
				OnPropertyChanged(nameof(CanShowNew));
			}

			if (args.Flags.HasFlag(BrowseUpdateFlags.NavigationCapabilities))
			{
				OnPropertyChanged(nameof(CanGoBack));
				OnPropertyChanged(nameof(CanGoForward));
				OnPropertyChanged(nameof(CanGoUp));
			}

			if (hadOperationError || args.Flags.HasFlag(BrowseUpdateFlags.Items) || args.Flags.HasFlag(BrowseUpdateFlags.Status))
			{
				OnPropertyChanged(nameof(StatusText));
			}

			var folderEmptyStateChanged = hadOperationError
				|| args.Flags.HasFlag(BrowseUpdateFlags.Items)
				|| args.Flags.HasFlag(BrowseUpdateFlags.Loading)
				|| args.Flags.HasFlag(BrowseUpdateFlags.Location)
				|| args.Flags.HasFlag(BrowseUpdateFlags.Status);
			if (folderEmptyStateChanged)
			{
				OnPropertyChanged(nameof(IsFolderEmpty));
			}
		}
		finally
		{
			_isApplyingUpdate = false;
			UiDiagnosticLog.Write(
				"FolderBrowserViewModel",
				$"Updated completed changes={args.ItemChanges.Count} items={Items.Count} loading={_browseAdapter.IsLoading} elapsedMs={Stopwatch.GetElapsedTime(updateStartTimestamp).TotalMilliseconds:F1}");
		}

		var invalidation = CommandStateInvalidation.None;
		if (selectionSynchronized)
		{
			invalidation |= CommandStateInvalidation.Selection;
		}

		if (args.Flags.HasFlag(BrowseUpdateFlags.Loading))
		{
			invalidation |= CommandStateInvalidation.Loading;
		}

		if (args.Flags.HasFlag(BrowseUpdateFlags.Location))
		{
			invalidation |= CommandStateInvalidation.Location;
		}

		if (args.Flags.HasFlag(BrowseUpdateFlags.NavigationCapabilities))
		{
			invalidation |= CommandStateInvalidation.Navigation;
		}

		if (args.Flags.HasFlag(BrowseUpdateFlags.ViewSettings))
		{
			invalidation |= CommandStateInvalidation.ViewSettings;
		}

		if (invalidation is not CommandStateInvalidation.None)
		{
			CommandManager.RefreshStates(invalidation);
		}
	}

	private void SetSelectionCore(IEnumerable<BrowseItemViewModel> selectedItems)
	{
		ArgumentNullException.ThrowIfNull(selectedItems);

		_browseAdapter.SetSelection(selectedItems);
		CommandManager.RefreshStates(CommandStateInvalidation.Selection);
	}

	private IReadOnlyList<string> GetSelectedFilePaths()
	{
		var selectedItems = SelectedItems;
		var paths = new List<string>(selectedItems.Count);
		foreach (var item in selectedItems)
		{
			if (!TryGetFileSystemPath(item.Reference, out var path))
			{
				return [];
			}

			paths.Add(path);
		}

		return paths;
	}

	private bool TryGetCurrentFileSystemFolder(out StorableReference folder)
	{
		if (Location is FolderLocation { Folder: { LastKnownAddress: { } address } }
			&& address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase)
			&& Directory.Exists(address.Value))
		{
			folder = ((FolderLocation)Location).Folder;

			return true;
		}

		folder = null!;

		return false;
	}

	private static bool TryGetFileSystemPath(StorableReference reference, out string path)
	{
		if (reference.LastKnownAddress is { } address
			&& address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase)
			&& Path.IsPathRooted(address.Value)
			&& (File.Exists(address.Value) || Directory.Exists(address.Value)))
		{
			path = address.Value;

			return true;
		}

		path = string.Empty;

		return false;
	}

	private async Task ExecuteStorageOperationAsync(StorageOperationRequest request, CancellationToken cancellationToken)
	{
		if (!_storageOperations.CanHandle(request))
		{
			throw new NotSupportedException($"No storage operation handler can handle '{request.GetType().Name}'.");
		}

		var result = await _storageOperations.ExecuteAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			throw result.Error ?? new IOException($"The storage operation '{request.GetType().Name}' failed.");
		}
	}

	private DeleteOperationRequest CreateDeleteRequest(StorableReference reference)
	{
		var permanently = _windowsSource is null || reference.SourceId != _windowsSource.SourceId;

		return new DeleteOperationRequest(reference, permanently);
	}

	private void RefreshLocationIcon()
	{
		Interlocked.Exchange(ref _locationIconCancellation, null)?.Cancel();
		SetProperty(ref _locationIcon, null, nameof(LocationIcon));

		var browseSession = _pane.BrowseSession;
		var source = browseSession.Context?.LocationModel?.Get<IThumbnailSource>();
		if (source is null || Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		_locationIconCancellation = cancellation;
		_ = LoadLocationIconAsync(source, browseSession.Generation, cancellation);
	}

	private async Task LoadLocationIconAsync(IThumbnailSource source, long generation, CancellationTokenSource cancellation)
	{
		try
		{
			var result = await source.GetThumbnailAsync(new ThumbnailRequest(LocationIconSize, ThumbnailMode.Icon), cancellation.Token).ConfigureAwait(false);
			if (result is null || !IsCurrentLocationIcon(generation, cancellation))
			{
				return;
			}

			await SetLocationIconOnUiAsync(result.Content, generation, cancellation).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			UiDiagnosticLog.Write("FolderBrowserViewModel", $"Location icon ERROR type={exception.GetType().Name}");
		}
		finally
		{
			Interlocked.CompareExchange(ref _locationIconCancellation, null, cancellation);
			cancellation.Dispose();
		}
	}

	private Task SetLocationIconOnUiAsync(ReadOnlyMemory<byte> encodedImage, long generation, CancellationTokenSource cancellation)
	{
		if (_dispatcher.HasThreadAccess)
		{
			return SetLocationIconAsync(encodedImage, generation, cancellation);
		}

		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_dispatcher.TryEnqueue(async () =>
		{
			try
			{
				await SetLocationIconAsync(encodedImage, generation, cancellation);
				completion.SetResult(true);
			}
			catch (Exception exception)
			{
				completion.SetException(exception);
			}
		}))
		{
			completion.SetException(new InvalidOperationException("The Files UI dispatcher rejected a location icon update."));
		}

		return completion.Task;
	}

	private async Task SetLocationIconAsync(ReadOnlyMemory<byte> encodedImage, long generation, CancellationTokenSource cancellation)
	{
		if (!IsCurrentLocationIcon(generation, cancellation))
		{
			return;
		}

		var image = await ThumbnailImageFactory.CreateAsync(encodedImage).ConfigureAwait(true);
		if (!IsCurrentLocationIcon(generation, cancellation))
		{
			return;
		}

		SetProperty(ref _locationIcon, image, nameof(LocationIcon));
	}

	private bool IsCurrentLocationIcon(long generation, CancellationTokenSource cancellation)
	{
		return Volatile.Read(ref _isDisposed) is 0 &&
			!cancellation.IsCancellationRequested &&
			ReferenceEquals(Volatile.Read(ref _locationIconCancellation), cancellation) &&
			_pane.BrowseSession.Generation == generation;
	}

	private bool ShouldSynchronizeSelection(CoreBrowseUpdatedEventArgs args)
	{
		if (args.SelectionChanged)
		{
			return true;
		}

		var selectedKeys = _browseAdapter.SelectedKeys;
		if (selectedKeys.Count is 0)
		{
			return false;
		}

		var selectedKeySet = selectedKeys.ToHashSet();
		foreach (var change in args.ItemChanges)
		{
			switch (change)
			{
				case BrowseItemViewModelsReset:
					return true;
				case BrowseItemViewModelAdded added when selectedKeySet.Contains(added.Item.Reference.GetKey()):
				case BrowseItemViewModelsAdded addedRange when addedRange.Items.Any(item => selectedKeySet.Contains(item.Reference.GetKey())):
				case BrowseItemViewModelReplaced replaced when selectedKeySet.Contains(replaced.Item.Reference.GetKey()):
					return true;
			}
		}

		return false;
	}

	private void RefreshItemsViewSource()
	{
		var propertyId = ViewSettings.GroupPropertyId;
		if (propertyId is null)
		{
			if (ItemGroups.Count is not 0)
			{
				ItemGroups.Clear();
			}
			if (ReferenceEquals(_itemsViewSource.Source, Items))
			{
				return;
			}

			_itemsViewSource = CreateItemsViewSource(Items, isGrouped: false);
			OnPropertyChanged(nameof(ItemsViewSource));

			return;
		}

		ItemGroups.ReplaceAll(BrowseItemGrouping.Create(Items, propertyId, ViewSettings.GroupDirection));
		_itemsViewSource = CreateItemsViewSource(ItemGroups, isGrouped: true);
		OnPropertyChanged(nameof(ItemsViewSource));
	}

	private void QueueGroupRefresh()
	{
		if (ViewSettings.GroupPropertyId is null || Interlocked.Exchange(ref _groupRefreshQueued, 1) is not 0)
		{
			return;
		}

		_ = QueueGroupRefreshAsync();
	}

	private async Task QueueGroupRefreshAsync()
	{
		try
		{
			await Task.Delay(TimeSpan.FromMilliseconds(50), _lifetime.Token).ConfigureAwait(false);
			if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
			{
				Interlocked.Exchange(ref _groupRefreshQueued, 0);
				if (Volatile.Read(ref _isDisposed) is 0)
				{
					if (_browseAdapter.IsLoading)
					{
						QueueGroupRefresh();
					}
					else
					{
						RefreshItemsViewSource();
					}
				}
			}))
			{
				Interlocked.Exchange(ref _groupRefreshQueued, 0);
			}
		}
		catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
		{
			Interlocked.Exchange(ref _groupRefreshQueued, 0);
		}
	}

	private void ApplyItemChanges(IReadOnlyList<BrowseItemViewModelChange> changes)
	{
		var changeIndex = 0;
		while (changeIndex < changes.Count)
		{
			if (changes[changeIndex] is BrowseItemViewModelsAdded addedRange)
			{
				if (addedRange.StartingIndex == Items.Count)
				{
					Items.AddRange(addedRange.Items);
				}
				else
				{
					Items.InsertRange(addedRange.StartingIndex, addedRange.Items);
				}

				changeIndex++;

				continue;
			}

			if (changes[changeIndex] is BrowseItemViewModelAdded firstAdded)
			{
				var addedItems = new List<BrowseItemViewModel> { firstAdded.Item };
				var nextChangeIndex = changeIndex + 1;
				var expectedIndex = firstAdded.Index + 1;
				while (nextChangeIndex < changes.Count && changes[nextChangeIndex] is BrowseItemViewModelAdded nextAdded && nextAdded.Index == expectedIndex)
				{
					addedItems.Add(nextAdded.Item);
					nextChangeIndex++;
					expectedIndex++;
				}

				if (addedItems.Count > 1)
				{
					if (firstAdded.Index == Items.Count)
					{
						Items.AddRange(addedItems);
					}
					else
					{
						Items.InsertRange(firstAdded.Index, addedItems);
					}

					UiDiagnosticLog.Write("FolderBrowserViewModel", $"Applied range index={firstAdded.Index} count={addedItems.Count} append={firstAdded.Index == Items.Count - addedItems.Count}");
					changeIndex = nextChangeIndex;

					continue;
				}
			}

			ApplyItemChange(changes[changeIndex]);
			changeIndex++;
		}
	}

	private bool CanApplyItemChanges(IReadOnlyList<BrowseItemViewModelChange> changes)
	{
		var itemCount = Items.Count;
		foreach (var change in changes)
		{
			switch (change)
			{
				case BrowseItemViewModelsAdded added when added.StartingIndex >= 0 && added.StartingIndex <= itemCount:
					itemCount += added.Items.Count;
					break;
				case BrowseItemViewModelAdded added when added.Index >= 0 && added.Index <= itemCount:
					itemCount++;
					break;
				case BrowseItemViewModelRemoved removed when removed.Index >= 0 && removed.Index < itemCount:
					itemCount--;
					break;
				case BrowseItemViewModelReplaced replaced when replaced.Index >= 0 && replaced.Index < itemCount:
					break;
				case BrowseItemViewModelMoved moved when moved.PreviousIndex >= 0 && moved.PreviousIndex < itemCount && moved.CurrentIndex >= 0 && moved.CurrentIndex < itemCount:
					break;
				case BrowseItemViewModelsReset reset:
					itemCount = reset.Items.Count;
					break;
				default:

					return false;
			}
		}

		return itemCount == _browseAdapter.Items.Count;
	}

	private void ApplyItemChange(BrowseItemViewModelChange change)
	{
		switch (change)
		{
			case BrowseItemViewModelAdded added:
				Items.Insert(added.Index, added.Item);
				break;
			case BrowseItemViewModelRemoved removed:
				Items.RemoveAt(removed.Index);
				break;
			case BrowseItemViewModelReplaced replaced:
				Items[replaced.Index] = replaced.Item;
				break;
			case BrowseItemViewModelMoved moved:
				Items.Move(moved.PreviousIndex, moved.CurrentIndex);
				break;
			case BrowseItemViewModelsReset reset:
				Items.ReplaceAll(reset.Items);
				break;
			default:
				throw new InvalidOperationException($"Unsupported browse item change '{change.GetType().Name}'.");
		}
	}

	private static bool ShouldReplaceItems(IReadOnlyList<BrowseItemViewModelChange> changes, bool wasLoading, bool isLoading)
	{
		if (changes.Any(static change => change is BrowseItemViewModelsReset))
		{
			return true;
		}

		if (!(wasLoading || isLoading) || changes.Count < BulkNotificationThreshold)
		{
			return false;
		}

		return !IsContiguousAddedRange(changes);
	}

	private static bool IsContiguousAddedRange(IReadOnlyList<BrowseItemViewModelChange> changes)
	{
		if (changes.Count is 0)
		{
			return false;
		}

		var expectedIndex = -1;
		foreach (var change in changes)
		{
			if (change is BrowseItemViewModelsAdded addedRange)
			{
				if (expectedIndex >= 0 && addedRange.StartingIndex != expectedIndex)
				{
					return false;
				}

				expectedIndex = addedRange.StartingIndex + addedRange.Items.Count;

				continue;
			}

			if (change is not BrowseItemViewModelAdded added)
			{
				return false;
			}

			if (expectedIndex >= 0 && added.Index != expectedIndex)
			{
				return false;
			}

			expectedIndex = added.Index + 1;
		}

		return true;
	}

	private static CollectionViewSource CreateItemsViewSource(object source, bool isGrouped)
	{
		return new CollectionViewSource
		{
			IsSourceGrouped = isGrouped,
			Source = source,
		};
	}

	private Task SetViewModeOnUiAsync(FolderViewMode mode)
	{
		if (_dispatcher.HasThreadAccess)
		{
			ViewMode = mode;

			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_dispatcher.TryEnqueue(() =>
		{
			try
			{
				ViewMode = mode;
				completion.SetResult(true);
			}
			catch (Exception exception)
			{
				completion.SetException(exception);
			}
		}))
		{
			completion.SetException(new InvalidOperationException("The Files UI dispatcher rejected a folder view update."));
		}

		return completion.Task;
	}

	private static FolderViewMode ToFolderViewMode(ViewLayoutMode mode) =>
		mode switch
		{
			ViewLayoutMode.Details => FolderViewMode.Details,
			ViewLayoutMode.List => FolderViewMode.List,
			ViewLayoutMode.Cards => FolderViewMode.Cards,
			ViewLayoutMode.Grid => FolderViewMode.Grid,
			ViewLayoutMode.Columns => FolderViewMode.Columns,
			_ => throw new InvalidOperationException($"Unsupported folder layout mode '{mode}'."),
		};

	private static ViewLayoutMode ToViewLayoutMode(FolderViewMode mode) =>
		mode switch
		{
			FolderViewMode.Details => ViewLayoutMode.Details,
			FolderViewMode.List => ViewLayoutMode.List,
			FolderViewMode.Cards => ViewLayoutMode.Cards,
			FolderViewMode.Grid => ViewLayoutMode.Grid,
			FolderViewMode.Columns => ViewLayoutMode.Columns,
			_ => throw new InvalidOperationException($"Unsupported folder view mode '{mode}'."),
		};

	private void EnsureActive()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);
	}
}
