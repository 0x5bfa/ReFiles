// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;
using Files.Adapters;
using Files.Commands;
using Files.Infrastructure;
using Files.Localization;
using Files.Core.Sessions;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Files.ViewModels;

public enum FolderViewMode
{
	Details,
	List,
	Cards,
	Grid,
	Columns,
}

public sealed class FolderBrowserViewModel : ObservableObject, IDisposable
{
	private const int BulkNotificationThreshold = 32;
	private const int LocationIconSize = 16;

	private readonly BrowsePresentationAdapter _browseAdapter;
	private readonly BrowsePaneSession _pane;

	private readonly IUIDispatcher _dispatcher;

	private readonly CancellationTokenSource _lifetime = new();

	private CollectionViewSource _itemsViewSource;

	private string? _operationError;
	private BitmapImage? _locationIcon;
	private CancellationTokenSource? _locationIconCancellation;

	private bool _isApplyingUpdate;

	private bool _wasLoading;
	private bool _wasBusy;

	private int _isDisposed;
	private int _groupRefreshQueued;

	private FolderViewMode _viewMode = FolderViewMode.Details;

	internal WindowCommandManager CommandManager { get; }

	public BulkObservableCollection<BrowseItemViewModel> Items { get; } = [];

	public BulkObservableCollection<BrowseItemGroupViewModel> ItemGroups { get; } = [];

	public CollectionViewSource ItemsViewSource => _itemsViewSource;

	public IReadOnlyList<DetailsColumnViewModel> DetailsColumns => _browseAdapter.DetailsColumns;

	public BrowseViewSettings ViewSettings => _browseAdapter.ViewSettings;

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

	public string LocationText => _browseAdapter.LocationText;

	public BrowseLocation? Location => _pane.Location;

	public string LocationDisplayName => _pane.BrowseSession.Context?.LocationModel?.Name ?? LocationText;

	public BitmapImage? LocationIcon => _locationIcon;

	public bool IsLoading => _browseAdapter.IsLoading;

	public bool IsBusy => _browseAdapter.IsBusy;

	public bool CanGoBack => _browseAdapter.CanGoBack;

	public bool CanGoForward => _browseAdapter.CanGoForward;

	public bool CanGoUp => _browseAdapter.CanGoUp;

	public bool CanRefresh => !IsLoading;

	public string StatusText =>
		_operationError
		?? _browseAdapter.ErrorMessage
		?? _browseAdapter.StatusText;

	public FolderBrowserViewModel(BrowsePaneSession pane, IStorageWorkspace workspace, IUIDispatcher dispatcher, WindowCommandManager commandManager)
	{
		ArgumentNullException.ThrowIfNull(pane);

		ArgumentNullException.ThrowIfNull(workspace);

		ArgumentNullException.ThrowIfNull(commandManager);

		ArgumentNullException.ThrowIfNull(dispatcher);

		CommandManager = commandManager;
		_pane = pane;
		_dispatcher = dispatcher;
		_browseAdapter = new BrowsePresentationAdapter(pane, workspace, dispatcher);
		_itemsViewSource = CreateItemsViewSource(Items, isGrouped: false);
		_viewMode = ToFolderViewMode(_browseAdapter.LayoutMode);
		_wasLoading = _browseAdapter.IsLoading;
		_wasBusy = _browseAdapter.IsBusy;
		_browseAdapter.Updated += BrowseAdapter_Updated;
	}

	public Task InitializeAsync(CancellationToken cancellationToken = default) =>
		_browseAdapter.InitializeAsync(cancellationToken);

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

	public void UpdateViewport(BrowseViewport viewport) =>
		_browseAdapter.UpdateViewport(viewport);

	public void SetSelection(IEnumerable<BrowseItemViewModel> selectedItems) =>
		_browseAdapter.SetSelection(selectedItems);

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
		return _browseAdapter.UpdateShowHiddenItemsAsync(showHiddenItems, cancellationToken);
	}

	public ValueTask SetShowFileExtensionsAsync(bool showFileExtensions, CancellationToken cancellationToken = default)
	{
		return _browseAdapter.UpdateShowFileExtensionsAsync(showFileExtensions, cancellationToken);
	}

	public void ReportOperationError(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);

		_operationError = exception.Message;
		OnPropertyChanged(nameof(StatusText));
	}

	public void ReportOperationCanceled()
	{
		_operationError = Strings.OperationCanceled.GetLocalized();
		OnPropertyChanged(nameof(StatusText));
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_browseAdapter.Updated -= BrowseAdapter_Updated;
		Interlocked.Exchange(ref _locationIconCancellation, null)?.Cancel();
		_lifetime.Cancel();
		_browseAdapter.Dispose();
		_lifetime.Dispose();
	}

	private void BrowseAdapter_Updated(object? sender, CoreBrowseUpdatedEventArgs args)
	{
		var updateStartTimestamp = Stopwatch.GetTimestamp();
		var itemCountBefore = Items.Count;
		var wasLoading = _wasLoading;
		var wasBusy = _wasBusy;
		_wasLoading = _browseAdapter.IsLoading;
		_wasBusy = _browseAdapter.IsBusy;
		_isApplyingUpdate = true;
		var refreshItemsViewSource = false;
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
				else
				{
					ApplyItemChanges(args.ItemChanges);
				}

				refreshItemsViewSource = true;
			}

			if (refreshItemsViewSource)
			{
				RefreshItemsViewSource();
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

			if (ShouldSynchronizeSelection(args))
			{
				OnPropertyChanged(nameof(SelectedKeys));
			}

			if (args.Flags.HasFlag(BrowseUpdateFlags.Location))
			{
				OnPropertyChanged(nameof(LocationText));
				OnPropertyChanged(nameof(LocationDisplayName));
				RefreshLocationIcon();
			}

			if (args.Flags.HasFlag(BrowseUpdateFlags.Loading))
			{
				OnPropertyChanged(nameof(IsLoading));
				OnPropertyChanged(nameof(IsBusy));
				OnPropertyChanged(nameof(CanRefresh));
			}
			else if (wasBusy != _browseAdapter.IsBusy)
			{
				OnPropertyChanged(nameof(IsBusy));
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
		}
		finally
		{
			_isApplyingUpdate = false;
			UiDiagnosticLog.Write(
				"FolderBrowserViewModel",
				$"Updated completed changes={args.ItemChanges.Count} items={Items.Count} loading={_browseAdapter.IsLoading} elapsedMs={Stopwatch.GetElapsedTime(updateStartTimestamp).TotalMilliseconds:F1}");
		}
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

			await SetLocationIconOnUiAsync(result.Content.ToArray(), generation, cancellation).ConfigureAwait(false);
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

	private Task SetLocationIconOnUiAsync(byte[] encodedImage, long generation, CancellationTokenSource cancellation)
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

	private async Task SetLocationIconAsync(byte[] encodedImage, long generation, CancellationTokenSource cancellation)
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
			ItemGroups.Clear();
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
					RefreshItemsViewSource();
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
