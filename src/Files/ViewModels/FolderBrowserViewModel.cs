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
using Files.StorageOperations;
using Files.Core.Sessions;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Windows;
using Files.Core.ViewSettings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
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
	private readonly StorageOperationTracker _operationTracker;
	private readonly AppSettingsService _appSettings;
	private readonly WindowsStorageSource? _windowsSource;
	private readonly WindowsShellNewMenu? _shellNewMenu;
	private readonly WindowsShellAppExtensionService? _shellAppExtensionService;
	private readonly WindowsShellContextualCommandService? _shellContextualCommandService;
	private readonly nint _ownerWindowHandle;

	private readonly IUIDispatcher _dispatcher;

	private readonly CancellationTokenSource _lifetime = new();
	private readonly HashSet<string> _pendingContextualCommandIds = new(StringComparer.OrdinalIgnoreCase);

	private CollectionViewSource _itemsViewSource;

	private string? _browseErrorMessage;
	private ImageSource? _locationIcon;
	private CancellationTokenSource? _locationIconCancellation;
	private CancellationTokenSource? _displaySettingsCancellation;
	private CancellationTokenSource? _contextualCommandRefreshCancellation;
	private Task _contextualCommandRefreshTask = Task.CompletedTask;
	private StorableReference? _contextualCommandRefreshLocation;
	private IReadOnlyList<StorableReference> _contextualCommandRefreshSelection = [];
	private IReadOnlyDictionary<string, WindowsShellContextualCommand> _contextualCommands = new Dictionary<string, WindowsShellContextualCommand>(StringComparer.OrdinalIgnoreCase);

	private bool _isApplyingUpdate;

	private bool _wasLoading;
	private bool _wasBusy;

	private int _isDisposed;
	private int _groupRefreshQueued;

	private FolderViewMode _viewMode = FolderViewMode.Details;

	internal WindowCommandManager CommandManager { get; }

	internal IUIDispatcher Dispatcher => _dispatcher;

	internal nint OwnerWindowHandle => _ownerWindowHandle;

	internal bool CanShowShellContextMenu => _ownerWindowHandle is not 0 && _shellAppExtensionService is not null;

	public BulkObservableCollection<BrowseItemViewModel> Items { get; } = [];

	public BulkObservableCollection<BrowseItemGroupViewModel> ItemGroups { get; } = [];

	public CollectionViewSource ItemsViewSource => _itemsViewSource;

	public IReadOnlyList<DetailsColumnViewModel> DetailsColumns => _browseAdapter.DetailsColumns;

	public BrowseViewSettings ViewSettings => _browseAdapter.ViewSettings;

	public bool ShowHiddenItems => _appSettings.ShowHiddenItems;

	public bool ShowFileExtensions => _appSettings.ShowFileExtensions;

	public double LayoutSize => _browseAdapter.ItemLayoutMetrics.LayoutSize;

	public double DetailsRowHeight => _browseAdapter.ItemLayoutMetrics.DetailsRowHeight;

	public double ListThumbnailSize => _browseAdapter.ItemLayoutMetrics.ListThumbnailSize;

	public double ListItemHeight => _browseAdapter.ItemLayoutMetrics.ListItemHeight;

	public double CardsThumbnailSize => _browseAdapter.ItemLayoutMetrics.CardsThumbnailSize;

	public double CardsItemHeight => _browseAdapter.ItemLayoutMetrics.CardsItemHeight;

	public double GridItemSize => _browseAdapter.ItemLayoutMetrics.GridItemSize;

	public double GridThumbnailSize => _browseAdapter.ItemLayoutMetrics.GridThumbnailSize;

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

	public bool CanCopy => !IsLoading && !IsBusy && GetSelectedShellClipboardItems().Count is not 0;

	public bool CanCut => CanCopy;

	public bool CanPaste => !IsLoading && !IsBusy && _windowsSource is not null && TryGetCurrentShellFolder(out _);

	public bool CanDelete => !IsLoading && !IsBusy && SelectedItems.Count is not 0 && SelectedItems.All(item => _storageOperations.CanHandle(CreateDeleteRequest(item.Reference)));

	public bool CanShowNew => !IsLoading && !IsBusy && _shellNewMenu is not null && TryGetCurrentFileSystemFolder(out _);

	internal bool SupportsItemSelection => Location is not null and not HomeLocation;

	internal bool CanSelectAllItems => SupportsItemSelection && !IsBusy && Items.Count > SelectedKeys.Count;

	internal bool CanInvertItemSelection => SupportsItemSelection && !IsBusy && Items.Count is not 0;

	internal bool CanClearItemSelection => SupportsItemSelection && !IsBusy && SelectedKeys.Count is not 0;

	public string LocationText => _browseAdapter.LocationText;

	public BrowseLocation? Location => _pane.Location;

	public string LocationDisplayName => _pane.BrowseSession.Context?.LocationModel?.Name ?? LocationText;

	public ImageSource? LocationIcon => _locationIcon;

	public bool IsLoading => _browseAdapter.IsLoading;

	public bool IsFolderEmpty => !IsLoading && Location is not null && Items.Count is 0 && _browseAdapter.ErrorMessage is null;

	public bool IsBusy => _browseAdapter.IsBusy;

	public bool CanGoBack => _browseAdapter.CanGoBack;

	public bool CanGoForward => _browseAdapter.CanGoForward;

	public bool CanGoUp => _browseAdapter.CanGoUp;

	public bool CanRefresh => !IsLoading;

	public string StatusText => _browseAdapter.StatusText;

	internal event EventHandler<OperationErrorEventArgs>? OperationErrorReported;

	internal FolderBrowserViewModel(BrowsePaneSession pane, IStorageWorkspace workspace, IStorageOperationService storageOperations, StorageOperationTracker operationTracker,
		AppSettingsService appSettings, IUIDispatcher dispatcher, WindowCommandManager commandManager, nint ownerWindowHandle = 0)
	{
		ArgumentNullException.ThrowIfNull(pane);

		ArgumentNullException.ThrowIfNull(workspace);

		ArgumentNullException.ThrowIfNull(storageOperations);

		ArgumentNullException.ThrowIfNull(operationTracker);

		ArgumentNullException.ThrowIfNull(appSettings);

		ArgumentNullException.ThrowIfNull(commandManager);

		ArgumentNullException.ThrowIfNull(dispatcher);

		CommandManager = commandManager;
		_pane = pane;
		_workspace = workspace;
		_storageOperations = storageOperations;
		_operationTracker = operationTracker;
		_appSettings = appSettings;
		_dispatcher = dispatcher;
		_browseAdapter = new BrowsePresentationAdapter(pane, workspace, dispatcher, ownerWindowHandle: ownerWindowHandle);
		_windowsSource = workspace.Sources.OfType<WindowsStorageSource>().FirstOrDefault();
		_shellNewMenu = _windowsSource is { } windowsSource
			? new WindowsShellNewMenu(windowsSource.Scheduler)
			: null;
		_shellAppExtensionService = _windowsSource is { } shellSource ? new WindowsShellAppExtensionService(shellSource) : null;
		_shellContextualCommandService = _windowsSource is { } contextualSource && ownerWindowHandle is not 0 ? new WindowsShellContextualCommandService(contextualSource) : null;
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
		QueueContextualCommandRefresh();
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

	public void SetSelection(IEnumerable<BrowseItemViewModel> selectedItems)
	{
		ArgumentNullException.ThrowIfNull(selectedItems);

		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		SetSelectionCore(selectedItems);
	}

	internal bool SupportsShellDragDrop(StorableReference reference)
	{
		ArgumentNullException.ThrowIfNull(reference);

		return _windowsSource is not null && reference.SourceId == _windowsSource.SourceId;
	}

	internal async Task<WindowsShellDragSource?> PrepareShellDragSourceAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (_windowsSource is null || selection.Count is 0 || selection.Any(reference => reference.SourceId != _windowsSource.SourceId))
		{
			return null;
		}

		return await _windowsSource.DragDrop.PrepareDragSourceAsync(selection, cancellationToken).ConfigureAwait(false);
	}

	internal async Task<WindowsShellDropTarget?> PrepareShellDropTargetAsync(StorableReference destination, bool background, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(destination);

		if (_windowsSource is null || destination.SourceId != _windowsSource.SourceId)
		{
			return null;
		}

		return await _windowsSource.DragDrop.PrepareDropTargetAsync(destination, background, cancellationToken).ConfigureAwait(false);
	}

	internal void SelectAllItems()
	{
		if (!CanSelectAllItems)
		{
			return;
		}

		SetSelectionCore(Items);
	}

	internal void InvertItemSelection()
	{
		if (!CanInvertItemSelection)
		{
			return;
		}

		var selectedKeys = SelectedKeys.ToHashSet();
		SetSelectionCore(Items.Where(item => !selectedKeys.Contains(item.Reference.GetKey())));
	}

	internal void ClearItemSelection()
	{
		if (!CanClearItemSelection)
		{
			return;
		}

		SetSelectionCore([]);
	}

	public async Task CopySelectionAsync(bool move, CancellationToken cancellationToken = default)
	{
		var selection = GetSelectedShellClipboardItems();
		if (_windowsSource is null || selection.Count is 0)
		{
			throw new NotSupportedException("Only Windows Shell items can be copied to the Windows clipboard.");
		}

		await _windowsSource.Clipboard.SetItemsAsync(selection, move, _ownerWindowHandle, cancellationToken).ConfigureAwait(false);
	}

	public async Task PasteFromClipboardAsync(CancellationToken cancellationToken = default)
	{
		if (_windowsSource is null || !TryGetCurrentShellFolder(out var destinationFolder))
		{
			throw new NotSupportedException("The current location cannot receive Windows Shell clipboard items.");
		}

		if (!await _windowsSource.DragDrop.PasteAsync(destinationFolder, _ownerWindowHandle, cancellationToken).ConfigureAwait(false))
		{
			return;
		}

		await RefreshAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task DeleteSelectionAsync(CancellationToken cancellationToken = default)
	{
		var selectedItems = SelectedItems.ToArray();
		if (selectedItems.Length is 0)
		{
			return;
		}

		var itemNames = selectedItems.Select(static item => item.Name).ToArray();
		await ExecuteTrackedStorageOperationBatchAsync(
			TrackedStorageOperationKind.Delete,
			itemNames,
			canCancel: true,
			canPause: false,
			(index, progress, operationControl, operationCancellation) => ExecuteStorageOperationAsync(CreateDeleteRequest(selectedItems[index].Reference), progress, operationCancellation, operationControl),
			cancellationToken).ConfigureAwait(false);

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

	internal CommandState GetContextualCommandState(string commandId, bool hideWhenShellDisabled)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

		if (!_contextualCommands.TryGetValue(commandId, out var command))
		{
			return new(false, false);
		}

		return new(!hideWhenShellDisabled || command.IsEnabled, command.IsEnabled);
	}

	internal async Task<bool> InvokeContextualCommandAsync(string commandId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

		if (_shellContextualCommandService is null || _ownerWindowHandle is 0)
		{
			return false;
		}

		if (!await WaitForContextualCommandRefreshAsync(commandId, cancellationToken) || !_contextualCommands.TryGetValue(commandId, out var command) || !command.IsEnabled || IsLoading || IsBusy)
		{
			return false;
		}

		var location = _contextualCommandRefreshLocation;
		var selection = _contextualCommandRefreshSelection.ToArray();
		var context = new WindowsShellInvocationContext(_ownerWindowHandle, GetContextualCommandWorkingDirectory());
		if (!await _shellContextualCommandService.InvokeAsync(location, selection, command, context, cancellationToken).ConfigureAwait(false))
		{
			return false;
		}

		await RefreshAsync(cancellationToken).ConfigureAwait(false);

		return true;
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

		OperationErrorReported?.Invoke(this, new OperationErrorEventArgs(exception.Message));
	}

	public void ReportOperationCanceled()
	{
		OperationErrorReported?.Invoke(this, new OperationErrorEventArgs(Strings.OperationCanceled.GetLocalized()));
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
		Interlocked.Exchange(ref _contextualCommandRefreshCancellation, null)?.Cancel();
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
			return [new NavigationToolbarBreadcrumbItem(LocationDisplayName, location, false, supportsShellDragDrop: SupportsShellDragDrop(location.Folder))];
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
			items.Add(new NavigationToolbarBreadcrumbItem(item.Text, item.Location, index < reversedItems.Count - 1 || leafHasChildren, supportsShellDragDrop: SupportsShellDragDrop(item.Location.Folder)));
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

					var thumbnail = await LoadBreadcrumbThumbnailAsync(root, cancellationToken).ConfigureAwait(false);
					items.Add(new NavigationToolbarBreadcrumbItem(root.Name, new FolderLocation(root.Reference), false, thumbnail, SupportsShellDragDrop(root.Reference)));
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

				var thumbnail = await LoadBreadcrumbThumbnailAsync(child, cancellationToken).ConfigureAwait(false);
				items.Add(new NavigationToolbarBreadcrumbItem(child.Name, new FolderLocation(child.Reference), false, thumbnail, SupportsShellDragDrop(child.Reference)));
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
				var thumbnail = await LoadBreadcrumbThumbnailAsync(child, cancellationToken).ConfigureAwait(false);
				items.Add(new NavigationToolbarBreadcrumbItem(child.Name, new ArchiveLocation(location.Archive, entryPath), false, thumbnail));
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

	private static async Task<ThumbnailResult?> LoadBreadcrumbThumbnailAsync(IStorableModel model, CancellationToken cancellationToken)
	{
		if (model.Get<IThumbnailSource>() is not { } source)
		{
			return null;
		}

		try
		{
			return await source.GetThumbnailAsync(new ThumbnailRequest(BreadcrumbThumbnailSize, ThumbnailMode.Icon), cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception error)
		{
			UiDiagnosticLog.Write("NavigationToolbar", $"Breadcrumb thumbnail failed for '{model.Reference.ItemId}': {error.GetType().Name}");

			return null;
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
		var browseErrorMessage = _browseAdapter.ErrorMessage;
		var browseErrorChanged = !string.Equals(_browseErrorMessage, browseErrorMessage, StringComparison.Ordinal);
		_browseErrorMessage = browseErrorMessage;
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

			if (args.Flags.HasFlag(BrowseUpdateFlags.Items) || args.Flags.HasFlag(BrowseUpdateFlags.Status))
			{
				OnPropertyChanged(nameof(StatusText));
			}

			var folderEmptyStateChanged = args.Flags.HasFlag(BrowseUpdateFlags.Items)
				|| args.Flags.HasFlag(BrowseUpdateFlags.Loading)
				|| args.Flags.HasFlag(BrowseUpdateFlags.Location)
				|| args.Flags.HasFlag(BrowseUpdateFlags.Status);
			if (folderEmptyStateChanged)
			{
				OnPropertyChanged(nameof(IsFolderEmpty));
			}

			if (browseErrorChanged && browseErrorMessage is not null)
			{
				OperationErrorReported?.Invoke(this, new OperationErrorEventArgs(browseErrorMessage));
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

		var contextualCommandScope = selectionSynchronized ? WindowsShellContextualCommandScope.Selection : WindowsShellContextualCommandScope.None;
		if (args.Flags.HasFlag(BrowseUpdateFlags.Location) || args.Flags.HasFlag(BrowseUpdateFlags.Items) || args.Flags.HasFlag(BrowseUpdateFlags.Loading))
		{
			contextualCommandScope = WindowsShellContextualCommandScope.All;
		}

		if (contextualCommandScope is not WindowsShellContextualCommandScope.None)
		{
			var coalesceMatchingRequest = selectionSynchronized && !args.Flags.HasFlag(BrowseUpdateFlags.Location) && !args.Flags.HasFlag(BrowseUpdateFlags.Items)
				&& wasLoading == _browseAdapter.IsLoading;
			QueueContextualCommandRefresh(contextualCommandScope, coalesceMatchingRequest: coalesceMatchingRequest);
		}
	}

	private void SetSelectionCore(IEnumerable<BrowseItemViewModel> selectedItems)
	{
		ArgumentNullException.ThrowIfNull(selectedItems);

		var selection = selectedItems.ToArray();
		_browseAdapter.SetSelection(selection);
		CommandManager.RefreshStates(CommandStateInvalidation.Selection);
		QueueContextualCommandRefresh(WindowsShellContextualCommandScope.Selection, selection.Select(static item => item.Reference).ToArray(), coalesceMatchingRequest: true);
	}

	private void QueueContextualCommandRefresh(WindowsShellContextualCommandScope scope = WindowsShellContextualCommandScope.All,
		IReadOnlyList<StorableReference>? selectionOverride = null, bool coalesceMatchingRequest = false)
	{
		if (!_dispatcher.HasThreadAccess)
		{
			if (Volatile.Read(ref _isDisposed) is 0)
			{
				_dispatcher.TryEnqueue(() => QueueContextualCommandRefresh(scope, selectionOverride, coalesceMatchingRequest));
			}

			return;
		}

		var location = (Location as FolderLocation)?.Folder;
		var selection = selectionOverride ?? SelectedItems.Select(static item => item.Reference).ToArray();
		if (coalesceMatchingRequest && IsSameContextualCommandRequest(location, selection) && !_contextualCommandRefreshTask.IsCompleted)
		{
			return;
		}

		_contextualCommandRefreshLocation = location;
		_contextualCommandRefreshSelection = selection;

		var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		Interlocked.Exchange(ref _contextualCommandRefreshCancellation, cancellation)?.Cancel();
		MarkContextualCommandsPending(scope);
		if (_shellContextualCommandService is null)
		{
			ApplyContextualCommands([], cancellation);
			_contextualCommandRefreshTask = Task.CompletedTask;
			Interlocked.CompareExchange(ref _contextualCommandRefreshCancellation, null, cancellation);
			cancellation.Dispose();

			return;
		}

		if (IsLoading)
		{
			_contextualCommandRefreshTask = Task.CompletedTask;
			Interlocked.CompareExchange(ref _contextualCommandRefreshCancellation, null, cancellation);
			cancellation.Dispose();

			return;
		}

		_contextualCommandRefreshTask = LoadContextualCommandsAsync(location, selection, cancellation);
	}

	private bool IsSameContextualCommandRequest(StorableReference? location, IReadOnlyList<StorableReference> selection)
	{
		return Equals(_contextualCommandRefreshLocation, location) && _contextualCommandRefreshSelection.SequenceEqual(selection);
	}

	private async Task<bool> WaitForContextualCommandRefreshAsync(string commandId, CancellationToken cancellationToken)
	{
		while (_pendingContextualCommandIds.Contains(commandId))
		{
			var refreshTask = _contextualCommandRefreshTask;
			if (refreshTask.IsCompleted)
			{
				return false;
			}

			await refreshTask.WaitAsync(cancellationToken);
		}

		return true;
	}

	private async Task LoadContextualCommandsAsync(StorableReference? location, IReadOnlyList<StorableReference> selection, CancellationTokenSource cancellation)
	{
		try
		{
			var commands = await _shellContextualCommandService!.GetCommandsAsync(location, selection, _ownerWindowHandle, cancellation.Token).ConfigureAwait(false);
			if (cancellation.IsCancellationRequested || Volatile.Read(ref _isDisposed) is not 0)
			{
				return;
			}

			await ApplyContextualCommandsAsync(commands, cancellation).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			UiDiagnosticLog.Write("FolderBrowserViewModel", $"Contextual commands ERROR type={exception.GetType().Name}");
			await CompleteContextualCommandRefreshAsync(cancellation).ConfigureAwait(false);
		}
		finally
		{
			Interlocked.CompareExchange(ref _contextualCommandRefreshCancellation, null, cancellation);
			cancellation.Dispose();
		}
	}

	private Task ApplyContextualCommandsAsync(IReadOnlyList<WindowsShellContextualCommand> commands, CancellationTokenSource cancellation)
	{
		if (_dispatcher.HasThreadAccess)
		{
			ApplyContextualCommands(commands, cancellation);

			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_dispatcher.TryEnqueue(() =>
		{
			ApplyContextualCommands(commands, cancellation);
			completion.SetResult();
		}))
		{
			completion.SetException(new InvalidOperationException("The Files UI dispatcher rejected contextual command updates."));
		}

		return completion.Task;
	}

	private void ApplyContextualCommands(IReadOnlyList<WindowsShellContextualCommand> commands, CancellationTokenSource cancellation)
	{
		if (cancellation.IsCancellationRequested || !ReferenceEquals(_contextualCommandRefreshCancellation, cancellation) || Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		var nextCommands = commands.ToDictionary(static command => command.Id, StringComparer.OrdinalIgnoreCase);
		var changedCommandIds = GetChangedContextualCommandIds(_contextualCommands, nextCommands, _pendingContextualCommandIds);
		_contextualCommands = nextCommands;
		_pendingContextualCommandIds.Clear();
		CommandManager.RefreshContextualStates(changedCommandIds);
	}

	private Task CompleteContextualCommandRefreshAsync(CancellationTokenSource cancellation)
	{
		if (_dispatcher.HasThreadAccess)
		{
			CompleteContextualCommandRefresh(cancellation);

			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_dispatcher.TryEnqueue(() =>
		{
			CompleteContextualCommandRefresh(cancellation);
			completion.SetResult();
		}))
		{
			completion.SetException(new InvalidOperationException("The Files UI dispatcher rejected contextual command completion."));
		}

		return completion.Task;
	}

	private void CompleteContextualCommandRefresh(CancellationTokenSource cancellation)
	{
		if (cancellation.IsCancellationRequested || !ReferenceEquals(_contextualCommandRefreshCancellation, cancellation) || Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		ApplyContextualCommands([], cancellation);
	}

	private void MarkContextualCommandsPending(WindowsShellContextualCommandScope scope)
	{
		foreach (var command in _contextualCommands.Values)
		{
			if ((command.Scope & scope) is not WindowsShellContextualCommandScope.None)
			{
				_pendingContextualCommandIds.Add(command.Id);
			}
		}
	}

	private static IReadOnlySet<string> GetChangedContextualCommandIds(IReadOnlyDictionary<string, WindowsShellContextualCommand> currentCommands,
		IReadOnlyDictionary<string, WindowsShellContextualCommand> nextCommands, IEnumerable<string> pendingCommandIds)
	{
		var changedCommandIds = pendingCommandIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var pair in currentCommands)
		{
			if (!nextCommands.TryGetValue(pair.Key, out var nextCommand) || pair.Value.IsEnabled != nextCommand.IsEnabled || pair.Value.Scope != nextCommand.Scope)
			{
				changedCommandIds.Add(pair.Key);
			}
		}

		foreach (var commandId in nextCommands.Keys)
		{
			if (!currentCommands.ContainsKey(commandId))
			{
				changedCommandIds.Add(commandId);
			}
		}

		return changedCommandIds;
	}

	private string? GetContextualCommandWorkingDirectory()
	{
		return Location is FolderLocation { Folder.LastKnownAddress: { } address } && address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase)
			? address.Value
			: null;
	}

	private IReadOnlyList<StorableReference> GetSelectedShellClipboardItems()
	{
		var selectedItems = SelectedItems;
		var selection = new List<StorableReference>(selectedItems.Count);
		foreach (var item in selectedItems)
		{
			if (_windowsSource is null || item.Reference.SourceId != _windowsSource.SourceId)
			{
				return [];
			}

			selection.Add(item.Reference);
		}

		return selection;
	}

	private bool TryGetCurrentShellFolder(out StorableReference folder)
	{
		if (_windowsSource is not null && Location is FolderLocation { Folder: var candidate } && candidate.SourceId == _windowsSource.SourceId)
		{
			folder = candidate;

			return true;
		}

		folder = null!;

		return false;
	}

	private bool TryGetCurrentFileSystemFolder(out StorableReference folder)
	{
		if (Location is FolderLocation { Folder: { LastKnownAddress: { } address } }
			&& address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase)
			&& Path.IsPathRooted(address.Value))
		{
			folder = ((FolderLocation)Location).Folder;

			return true;
		}

		folder = null!;

		return false;
	}

	private async Task ExecuteTrackedStorageOperationBatchAsync(TrackedStorageOperationKind kind, IReadOnlyList<string> itemNames, bool canCancel, bool canPause,
		Func<int, IProgress<StorageOperationProgress>, IStorageOperationControl, CancellationToken, Task> execute, CancellationToken cancellationToken,
		IReadOnlyList<long>? itemByteCounts = null, string? destinationPath = null)
	{
		ArgumentNullException.ThrowIfNull(itemNames);

		ArgumentNullException.ThrowIfNull(execute);

		if (itemByteCounts is not null && itemByteCounts.Count != itemNames.Count)
		{
			throw new ArgumentException("The byte count must correspond to every batch item.", nameof(itemByteCounts));
		}

		if (itemNames.Count is 0)
		{
			return;
		}

		var totalBatchBytes = itemByteCounts?.Aggregate(0L, static (total, itemBytes) => checked(total + itemBytes));
		var completedBatchBytes = 0L;
		var operation = _operationTracker.StartOperation(kind, itemNames.Count, itemNames[0], canCancel, cancellationToken, destinationPath, canPause);
		try
		{
			for (var index = 0; index < itemNames.Count; index++)
			{
				operation.CancellationToken.ThrowIfCancellationRequested();
				var currentItemBytes = itemByteCounts?[index];
				operation.Report(index, itemNames[index], totalBatchBytes.HasValue ? completedBatchBytes : null, totalBatchBytes, totalBatchBytes.HasValue);
				var progress = new StorageOperationBatchProgress(operation, index, itemNames[index], totalBatchBytes.HasValue ? completedBatchBytes : null, currentItemBytes, totalBatchBytes);
				await execute(index, progress, operation, operation.CancellationToken).ConfigureAwait(false);
				completedBatchBytes += currentItemBytes ?? 0;
				operation.Report(index + 1, itemNames[index], totalBatchBytes.HasValue ? completedBatchBytes : null, totalBatchBytes, totalBatchBytes.HasValue);
			}

			operation.Complete();
		}
		catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
		{
			operation.MarkCanceled();

			throw;
		}
		catch (Exception error)
		{
			operation.Fail(error);

			throw;
		}
	}

	private async Task ExecuteStorageOperationAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress, CancellationToken cancellationToken,
		IStorageOperationControl? operationControl = null)
	{
		if (!_storageOperations.CanHandle(request))
		{
			throw new NotSupportedException($"No storage operation handler can handle '{request.GetType().Name}'.");
		}

		var result = await _storageOperations.ExecuteAsync(request, progress, cancellationToken, operationControl).ConfigureAwait(false);
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

			await SetLocationIconOnUiAsync(result, generation, cancellation).ConfigureAwait(false);
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

	private Task SetLocationIconOnUiAsync(ThumbnailResult thumbnail, long generation, CancellationTokenSource cancellation)
	{
		if (_dispatcher.HasThreadAccess)
		{
			return SetLocationIconAsync(thumbnail, generation, cancellation);
		}

		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_dispatcher.TryEnqueue(async () =>
		{
			try
			{
				await SetLocationIconAsync(thumbnail, generation, cancellation);
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

	private async Task SetLocationIconAsync(ThumbnailResult thumbnail, long generation, CancellationTokenSource cancellation)
	{
		if (!IsCurrentLocationIcon(generation, cancellation))
		{
			return;
		}

		var image = await ThumbnailImageFactory.CreateAsync(thumbnail).ConfigureAwait(true);
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
