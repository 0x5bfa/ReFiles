// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.Globalization;
using Files.Infrastructure;
using Files.ViewModels;
using Files.Core.Sessions;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Files.Core.ViewSettings;
using Microsoft.UI.Dispatching;

namespace Files.Adapters;

internal sealed class BrowsePresentationAdapter : IDisposable, IAsyncDisposable
{
	private const int MaxItemsPerDrain = 128;
	private const int MaxThumbnailsPerDrain = 8;
	private static readonly TimeSpan UiDrainBudget = TimeSpan.FromMilliseconds(4);

	private readonly BrowsePaneSession _pane;
	private readonly IStorageWorkspace _workspace;
	private readonly IUIDispatcher _dispatcher;
	private readonly IBrowsePrefetchCoordinator _prefetch;
	private readonly BrowsePresentationText _text;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly SemaphoreSlim _thumbnailDecodeGate = new(2);
	private readonly Lock _thumbnailTaskLock = new();
	private readonly HashSet<Task> _thumbnailTasks = [];
	private readonly Lock _pendingLock = new();
	private readonly LinkedList<PendingItemBatch> _pendingItemBatches = new();
	private readonly Dictionary<StorableKey, ThumbnailResult?> _pendingThumbnails = [];
	private readonly Dictionary<StorableKey, IReadOnlyDictionary<string, object?>> _pendingProperties = [];
	private readonly List<BrowseItemViewModel> _items = [];
	private readonly Lock _itemsLock = new();
	private readonly Dictionary<StorableKey, BrowseItemViewModel> _itemsByKey = [];
	private readonly Lock _locationNavigationLock = new();
	private PendingState? _pendingState;
	private PendingColumns? _pendingColumns;
	private IReadOnlyList<StorableKey>? _pendingSelection;
	private LocationNavigation? _locationNavigation;
	private ColumnLoad? _columnLoad;
	private ColumnCache? _columnCache;
	private IReadOnlyList<DetailsColumnViewModel> _detailsColumns = CreateFallbackColumns();
	private BrowseViewSettings _viewSettings;
	private long _appliedItemsVersion = -1;
	private long _diagnosticLongestDrainTicks;
	private int _diagnosticDrainSequence;
	private int _diagnosticDispatcherEnqueueCount;
	private int _diagnosticItemViewModelCount;
	private int _diagnosticPropertyChangeCount;
	private int _diagnosticThumbnailDisplayCount;
	private bool _drainQueued;
	private int _isApplyingItemBatch;
	private int _isApplyingDefaultColumns;
	private int _isDisposed;

	public BrowsePresentationAdapter(BrowsePaneSession pane, IStorageWorkspace workspace, IUIDispatcher dispatcher, IBrowsePrefetchCoordinator? prefetch = null, BrowsePresentationText? text = null)
	{
		ArgumentNullException.ThrowIfNull(pane);
		ArgumentNullException.ThrowIfNull(workspace);
		ArgumentNullException.ThrowIfNull(dispatcher);

		_pane = pane;
		_workspace = workspace;
		_dispatcher = dispatcher;
		_prefetch = prefetch ?? new BrowsePrefetchCoordinator(_pane.BrowseSession);
		_text = text ?? BrowsePresentationText.CreateLocalized();
		_viewSettings = _pane.BrowseSession.ViewSettings;

		SelectedKeys = Array.Empty<StorableKey>();
		LocationText = _text.Home;
		_pane.NavigationStateChanged += Pane_StateChanged;
		_pane.BrowseSession.ItemsChanged += BrowseSession_ItemsChanged;
		_pane.BrowseSession.ItemPresentationChanged +=
			BrowseSession_ItemPresentationChanged;
		_pane.BrowseSession.SelectionChanged += BrowseSession_SelectionChanged;
		QueueInitialSnapshot();
		UiDiagnosticLog.Write("BrowsePresentationAdapter", $"created items={_items.Count} loading={IsLoading}");
	}

	public IReadOnlyList<StorableKey> SelectedKeys { get; private set; }

	public string LocationText { get; private set; }

	public string? ErrorMessage { get; private set; }

	public bool IsLoading { get; private set; }

	public bool IsBusy => IsLoading || HasPendingItemUiWork();

	public bool CanGoBack => _pane.CanGoBack;

	public bool CanGoForward => _pane.CanGoForward;

	public bool CanGoUp => _pane.CanGoUp;

	public BrowseViewSettings ViewSettings => _viewSettings;

	public ViewLayoutMode LayoutMode => ViewSettings.LayoutMode;

	public string StatusText =>
		ErrorMessage
		?? string.Format(CultureInfo.CurrentCulture, _items.Count is 1 ? _text.ItemCountSingle : _text.ItemCountPlural, _items.Count);

	public event EventHandler<CoreBrowseUpdatedEventArgs>? Updated;

	public IReadOnlyList<BrowseItemViewModel> Items => _items;

	public IReadOnlyList<DetailsColumnViewModel> DetailsColumns => _detailsColumns;

	internal int CreatedItemViewModelCount => Volatile.Read(ref _diagnosticItemViewModelCount);

	internal IReadOnlyList<BrowseItemViewModel> GetItems(IReadOnlyList<StorableKey> keys)
	{
		ArgumentNullException.ThrowIfNull(keys);

		lock (_itemsLock)
		{
			var items = new List<BrowseItemViewModel>(keys.Count);
			foreach (var key in keys)
			{
				if (_itemsByKey.TryGetValue(key, out var item))
				{
					items.Add(item);
				}
			}

			return items.ToArray();
		}
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("BrowsePresentationAdapter", "Initialize START");
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		try
		{
			await NavigateToLocationAsync(HomeLocation.Instance, linkedCancellation.Token).ConfigureAwait(false);
		}
		finally
		{
			UiDiagnosticLog.Write("BrowsePresentationAdapter", $"Initialize END elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1} items={_items.Count} loading={IsLoading}");
		}
	}

	public Task NavigateHomeAsync(CancellationToken cancellationToken = default) =>
		InitializeAsync(cancellationToken);

	public async Task NavigateToPathAsync(string path, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("BrowsePresentationAdapter", $"NavigateToPath START path={path}");

		if (string.Equals(path, _text.Home, StringComparison.OrdinalIgnoreCase) || string.Equals(path, "Home", StringComparison.OrdinalIgnoreCase))
		{
			await InitializeAsync(cancellationToken).ConfigureAwait(false);

			return;
		}

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		try
		{
			var resolveStartTimestamp = Stopwatch.GetTimestamp();
			var model = await _workspace.ResolveAsync(new StorageAddress("file", path), linkedCancellation.Token).ConfigureAwait(false);
			UiDiagnosticLog.Write("BrowsePresentationAdapter", $"NavigateToPath resolved elapsedMs={Stopwatch.GetElapsedTime(resolveStartTimestamp).TotalMilliseconds:F1} model={model.GetType().Name}");
			try
			{
				if (model is not IFolderModel)
				{
					throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, _text.NotFolderFormat, path));
				}

				await NavigateToLocationAsync(new FolderLocation(model.Reference), linkedCancellation.Token).ConfigureAwait(false);
			}
			finally
			{
				await model.DisposeAsync().ConfigureAwait(false);
			}
		}
		finally
		{
			UiDiagnosticLog.Write("BrowsePresentationAdapter", $"NavigateToPath END elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1} items={_items.Count} loading={IsLoading}");
		}
	}

	public async Task NavigateToItemAsync(BrowseItemViewModel item, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(item);

		if (!item.IsFolder)
		{
			return;
		}

		await NavigateToReferenceAsync(item.Reference, cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task NavigateToReferenceAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(reference);

		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("BrowsePresentationAdapter", $"NavigateToReference START id={reference.ItemId}");

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		try
		{
			await NavigateToLocationAsync(new FolderLocation(reference), linkedCancellation.Token).ConfigureAwait(false);
		}
		finally
		{
			UiDiagnosticLog.Write("BrowsePresentationAdapter", $"NavigateToReference END elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1} items={_items.Count} loading={IsLoading}");
		}
	}

	public async Task GoBackAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("BrowsePresentationAdapter", "GoBack START");
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		try
		{
			await _pane.GoBackAsync(linkedCancellation.Token).ConfigureAwait(false);
		}
		finally
		{
			UiDiagnosticLog.Write("BrowsePresentationAdapter", $"GoBack END elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1} items={_items.Count}");
		}
	}

	public async Task GoForwardAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("BrowsePresentationAdapter", "GoForward START");
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		try
		{
			await _pane.GoForwardAsync(linkedCancellation.Token).ConfigureAwait(false);
		}
		finally
		{
			UiDiagnosticLog.Write("BrowsePresentationAdapter", $"GoForward END elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1} items={_items.Count}");
		}
	}

	public async Task GoUpAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("BrowsePresentationAdapter", "GoUp START");
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		try
		{
			await _pane.GoUpAsync(linkedCancellation.Token).ConfigureAwait(false);
		}
		finally
		{
			UiDiagnosticLog.Write("BrowsePresentationAdapter", $"GoUp END elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1} items={_items.Count}");
		}
	}

	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("BrowsePresentationAdapter", "Refresh START");
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		try
		{
			await _pane.RefreshAsync(linkedCancellation.Token).ConfigureAwait(false);
		}
		finally
		{
			UiDiagnosticLog.Write("BrowsePresentationAdapter", $"Refresh END elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1} items={_items.Count}");
		}
	}

	public void UpdateViewport(BrowseViewport viewport)
	{
		EnsureActive();

		UiDiagnosticLog.Write("BrowsePresentationAdapter", $"UpdateViewport first={viewport.FirstVisibleIndex} visible={viewport.VisibleCount} lookAhead={viewport.LookAheadCount}");
		_prefetch.UpdateViewport(viewport, _pane.BrowseSession.ViewSettings, _pane.BrowseSession.Generation);
	}

	public async ValueTask UpdateLayoutModeAsync(ViewLayoutMode mode, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		if (!Enum.IsDefined(mode))
		{
			throw new ArgumentOutOfRangeException(nameof(mode));
		}

		var currentSettings = _pane.BrowseSession.ViewSettings;
		if (currentSettings.LayoutMode == mode)
		{
			return;
		}

		var settings = new BrowseViewSettings(
			mode,
			currentSettings.Columns,
			currentSettings.SortPropertyId,
			currentSettings.SortDirection,
			currentSettings.ItemSize,
			currentSettings.GroupPropertyId,
			currentSettings.GroupDirection);

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await _pane.BrowseSession.UpdateViewSettingsAsync(settings, linkedCancellation.Token).ConfigureAwait(false);
	}

	public async ValueTask UpdateItemSizeAsync(double itemSize, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		if (!double.IsFinite(itemSize) || itemSize < 1 || itemSize > 5)
		{
			throw new ArgumentOutOfRangeException(nameof(itemSize));
		}

		var currentSettings = _pane.BrowseSession.ViewSettings;
		if (currentSettings.ItemSize == itemSize)
		{
			return;
		}

		var settings = new BrowseViewSettings(
			currentSettings.LayoutMode,
			currentSettings.Columns,
			currentSettings.SortPropertyId,
			currentSettings.SortDirection,
			itemSize,
			currentSettings.GroupPropertyId,
			currentSettings.GroupDirection);

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await _pane.BrowseSession.UpdateViewSettingsAsync(settings, linkedCancellation.Token).ConfigureAwait(false);
	}

	public async ValueTask UpdateDisplaySettingsAsync(BrowseDisplaySettings settings, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(settings);

		if (_pane.BrowseSession.DisplaySettings == settings)
		{
			return;
		}

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await _pane.BrowseSession.UpdateDisplaySettingsAsync(settings, linkedCancellation.Token).ConfigureAwait(false);
		if (_pane.BrowseSession.Location is null)
		{
			return;
		}

		await _pane.BrowseSession.RefreshAsync(linkedCancellation.Token).ConfigureAwait(false);
	}

	public async ValueTask UpdateColumnsAsync(IEnumerable<ViewColumnSettings> columns, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(columns);

		var currentSettings = _pane.BrowseSession.ViewSettings;
		var columnArray = columns.ToArray();
		if (currentSettings.Columns.SequenceEqual(columnArray))
		{
			return;
		}

		var settings = new BrowseViewSettings(
			currentSettings.LayoutMode,
			columnArray,
			currentSettings.SortPropertyId,
			currentSettings.SortDirection,
			currentSettings.ItemSize,
			currentSettings.GroupPropertyId,
			currentSettings.GroupDirection);
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await _pane.BrowseSession.UpdateViewSettingsAsync(settings, linkedCancellation.Token).ConfigureAwait(false);
	}

	public async ValueTask UpdateSortAsync(string propertyId, ViewSortDirection direction, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
		if (!Enum.IsDefined(direction))
		{
			throw new ArgumentOutOfRangeException(nameof(direction));
		}

		var currentSettings = _pane.BrowseSession.ViewSettings;
		if (string.Equals(currentSettings.SortPropertyId, propertyId, StringComparison.Ordinal) && currentSettings.SortDirection == direction)
		{
			return;
		}

		var settings = new BrowseViewSettings(
			currentSettings.LayoutMode,
			currentSettings.Columns,
			propertyId,
			direction,
			currentSettings.ItemSize,
			currentSettings.GroupPropertyId,
			currentSettings.GroupDirection);
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await _pane.BrowseSession.UpdateViewSettingsAsync(settings, linkedCancellation.Token).ConfigureAwait(false);
	}

	public async ValueTask UpdateGroupingAsync(string? propertyId, ViewSortDirection direction, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		if (propertyId is not null)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
		}

		if (!Enum.IsDefined(direction))
		{
			throw new ArgumentOutOfRangeException(nameof(direction));
		}

		var currentSettings = _pane.BrowseSession.ViewSettings;
		if (string.Equals(currentSettings.GroupPropertyId, propertyId, StringComparison.Ordinal) && currentSettings.GroupDirection == direction)
		{
			return;
		}

		var settings = new BrowseViewSettings(
			currentSettings.LayoutMode,
			currentSettings.Columns,
			currentSettings.SortPropertyId,
			currentSettings.SortDirection,
			currentSettings.ItemSize,
			propertyId,
			direction);
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await _pane.BrowseSession.UpdateViewSettingsAsync(settings, linkedCancellation.Token).ConfigureAwait(false);
	}

	public void SetSelection(IEnumerable<BrowseItemViewModel> selectedItems)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(selectedItems);

		var selectedKeys = selectedItems
			.Select(static item => item.Reference.GetKey())
			.ToArray();
		var focusedKey = selectedKeys.FirstOrDefault();
		_pane.BrowseSession.SetSelection(selectedKeys, selectedKeys.Length is 0 ? null : focusedKey, selectedKeys.Length is 0 ? null : focusedKey);
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

		Exception? prefetchError = null;
		try
		{
			await _prefetch.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception error)
		{
			prefetchError = error;
		}

		_pane.NavigationStateChanged -= Pane_StateChanged;
		_pane.BrowseSession.ItemsChanged -= BrowseSession_ItemsChanged;
		_pane.BrowseSession.ItemPresentationChanged -=
			BrowseSession_ItemPresentationChanged;
		_pane.BrowseSession.SelectionChanged -= BrowseSession_SelectionChanged;
		ColumnLoad? columnLoad;
		lock (_pendingLock)
		{
			columnLoad = _columnLoad;
			_columnCache = null;
		}

		Task? locationNavigationTask;
		lock (_locationNavigationLock)
		{
			locationNavigationTask = _locationNavigation?.Task;
		}

		columnLoad?.Cancel();
		_lifetime.Cancel();
		await Task.WhenAll(ObserveBackgroundTaskAsync(columnLoad?.Task), ObserveBackgroundTaskAsync(locationNavigationTask)).ConfigureAwait(false);
		Task[] thumbnailTasks;
		lock (_thumbnailTaskLock)
		{
			thumbnailTasks = _thumbnailTasks.ToArray();
		}

		if (thumbnailTasks.Length is not 0)
		{
			await Task.WhenAll(thumbnailTasks).ConfigureAwait(false);
		}

		_lifetime.Dispose();
		_thumbnailDecodeGate.Dispose();
		lock (_pendingLock)
		{
			_pendingItemBatches.Clear();
			_pendingThumbnails.Clear();
			_pendingProperties.Clear();
			_pendingState = null;
			_pendingColumns = null;
			_pendingSelection = null;
		}

		Updated = null;
		UiDiagnosticLog.Write(
			"BrowsePresentationAdapter",
			$"disposed itemViewModels={_diagnosticItemViewModelCount} dispatcherEnqueues={_diagnosticDispatcherEnqueueCount} " +
			$"propertyNotifications={_diagnosticPropertyChangeCount} thumbnailsDisplayed={_diagnosticThumbnailDisplayCount} " +
			$"longestUiMs={TimeSpan.FromTicks(Volatile.Read(ref _diagnosticLongestDrainTicks)).TotalMilliseconds:F1}");
		if (prefetchError is not null)
		{
			throw prefetchError;
		}
	}

	private void Pane_StateChanged(object? sender, EventArgs args)
	{
		var session = _pane.BrowseSession;
		lock (_pendingLock)
		{
			_pendingState = new PendingState(session.Generation, session.IsLoading, session.Error?.Message, GetLocationText(session.Location), session.ViewSettings);
		}

		StartColumnsLoad();
		ScheduleDrain();
	}

	private void BrowseSession_ItemsChanged(object? sender, BrowseItemsChangedEventArgs args)
	{
		var generation = _pane.BrowseSession.Generation;
		var projectionStartTimestamp = Stopwatch.GetTimestamp();
		var changes = ProjectChanges(args.Changes);
		UiDiagnosticLog.Write(
			"BrowsePresentationAdapter",
			$"ItemsChanged version={args.Version} previous={args.PreviousVersion} changes={args.Changes.Count} projected={changes.Count} " +
			$"projectionMs={Stopwatch.GetElapsedTime(projectionStartTimestamp).TotalMilliseconds:F1} coreItems={_pane.BrowseSession.Items.Count}");
		lock (_pendingLock)
		{
			_pendingItemBatches.AddLast(new PendingItemBatch(generation, args.PreviousVersion, args.Version, changes));
		}

		ScheduleDrain();
	}

	private void BrowseSession_SelectionChanged(object? sender, EventArgs args)
	{
		var selection = _pane.BrowseSession.Selection.SelectedKeys.ToArray();
		lock (_pendingLock)
		{
			_pendingSelection = selection;
		}

		ScheduleDrain();
	}

	private void BrowseSession_ItemPresentationChanged(object? sender, BrowseItemPresentationChangedEventArgs args)
	{
		UiDiagnosticLog.Write(
			"BrowsePresentationAdapter",
			$"ItemPresentationChanged key={args.Key} changes={args.Changed} hasProperties={args.Presentation.Properties.Count is not 0} " +
			$"hasThumbnail={args.Presentation.Thumbnail is not null}");
		lock (_pendingLock)
		{
			if (args.Changed.HasFlag(BrowseItemPresentationChangeFlags.Properties) && args.Presentation.Properties.Count is not 0)
			{
				_pendingProperties[args.Key] = args.Presentation.Properties;
			}

			if (args.Changed.HasFlag(BrowseItemPresentationChangeFlags.Thumbnail))
			{
				_pendingThumbnails[args.Key] = args.Presentation.Thumbnail;
			}
		}

		ScheduleDrain();
	}

	private void QueueInitialSnapshot()
	{
		var session = _pane.BrowseSession;
		var resetChanges = ProjectReset(session.Items);
		UiDiagnosticLog.Write("BrowsePresentationAdapter", $"QueueInitialSnapshot items={session.Items.Count} version={session.ItemsVersion}");
		lock (_pendingLock)
		{
			_pendingItemBatches.AddLast(new PendingItemBatch(session.Generation, -1, session.ItemsVersion, resetChanges));
			_pendingState = new PendingState(session.Generation, session.IsLoading, session.Error?.Message, GetLocationText(session.Location), session.ViewSettings);
			_pendingSelection = session.Selection.SelectedKeys.ToArray();
			foreach (var item in session.Items)
			{
				var key = item.Reference.GetKey();
				if (session.TryGetPresentation(key, out var presentation))
				{
					if (presentation.Properties.Count is not 0)
					{
						_pendingProperties[key] = presentation.Properties;
					}

					if (presentation.Thumbnail is not null)
					{
						_pendingThumbnails[key] = presentation.Thumbnail;
					}
				}
			}
		}

		ScheduleDrain();
	}

	private void ScheduleDrain(DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
	{
		int pendingBatchCount;
		int pendingThumbnailCount;
		lock (_pendingLock)
		{
			if (_drainQueued || Volatile.Read(ref _isDisposed) is not 0)
			{
				return;
			}

			_drainQueued = true;
			pendingBatchCount = _pendingItemBatches.Count;
			pendingThumbnailCount = _pendingThumbnails.Count;
		}

		if (!_dispatcher.TryEnqueue(priority, DrainPendingUpdates))
		{
			lock (_pendingLock)
			{
				_drainQueued = false;
			}

			if (Volatile.Read(ref _isDisposed) is 0)
			{
				throw new InvalidOperationException("The Files UI dispatcher rejected a Core update.");
			}
		}
		else
		{
			var enqueueCount = Interlocked.Increment(ref _diagnosticDispatcherEnqueueCount);
			UiDiagnosticLog.Write("BrowsePresentationAdapter", $"Drain queued enqueueCount={enqueueCount} pendingBatches={pendingBatchCount} pendingThumbnails={pendingThumbnailCount}");
		}
	}

	private void DrainPendingUpdates()
	{
		var drainStartTimestamp = Stopwatch.GetTimestamp();
		var drainSequence = Interlocked.Increment(ref _diagnosticDrainSequence);
		var drainDeadline = drainStartTimestamp + Math.Max(1L, (long)(Stopwatch.Frequency * UiDrainBudget.TotalSeconds));
		var wasBusy = IsBusy;
		PendingState? state;
		PendingColumns? columns;
		IReadOnlyList<StorableKey>? selection;
		KeyValuePair<StorableKey, IReadOnlyDictionary<string, object?>>[] properties;
		KeyValuePair<StorableKey, ThumbnailResult?>[] thumbnails;
		lock (_pendingLock)
		{
			if (Volatile.Read(ref _isDisposed) is not 0)
			{
				return;
			}

			state = _pendingState;
			_pendingState = null;
			columns = _pendingColumns;
			_pendingColumns = null;
			selection = _pendingSelection;
			_pendingSelection = null;
			properties = TakePendingPropertiesLocked();
			thumbnails = TakePendingThumbnailsLocked();
			_drainQueued = false;
		}

		var appliedChanges = new List<BrowseItemViewModelChange>();
		var drainedItemCount = 0;
		var hasPendingItemBatches = false;
		while (drainedItemCount < MaxItemsPerDrain && Stopwatch.GetTimestamp() < drainDeadline)
		{
			PendingItemBatch? batch;
			lock (_pendingLock)
			{
				batch = TakeNextPendingItemBatchLocked(MaxItemsPerDrain - drainedItemCount, _pane.BrowseSession.Generation, out hasPendingItemBatches);
			}

			if (batch is null)
			{
				break;
			}

			drainedItemCount += GetItemCount(batch.Changes);
			Interlocked.Exchange(ref _isApplyingItemBatch, 1);
			try
			{
				if (batch.Generation != _pane.BrowseSession.Generation)
				{
					continue;
				}

				if (batch.Version <= _appliedItemsVersion)
				{
					continue;
				}

				if (_appliedItemsVersion >= 0 && batch.PreviousVersion != _appliedItemsVersion)
				{
					ResetFromCurrentSession(appliedChanges);
					break;
				}

				if (!TryApplyChanges(batch.Changes, appliedChanges))
				{
					ResetFromCurrentSession(appliedChanges);
					break;
				}

				if (batch.Generation != _pane.BrowseSession.Generation)
				{
					_appliedItemsVersion = -1;
					appliedChanges.Clear();
					break;
				}

				if (batch.IsComplete)
				{
					_appliedItemsVersion = batch.Version;
				}
			}
			finally
			{
				Interlocked.Exchange(ref _isApplyingItemBatch, 0);
			}
		}

		lock (_pendingLock)
		{
			hasPendingItemBatches = _pendingItemBatches.Count is not 0;
		}

		UiDiagnosticLog.Write(
			"BrowsePresentationAdapter",
			$"Drain START sequence={drainSequence} items={drainedItemCount} thumbnails={thumbnails.Length} hasPending={hasPendingItemBatches}");

		foreach (var property in properties)
		{
			ApplyProperties(property.Key, property.Value);
		}

		var previousIsLoading = IsLoading;
		var previousLocationText = LocationText;
		var previousErrorMessage = ErrorMessage;
		var previousViewSettings = ViewSettings;
		var previousCanGoBack = CanGoBack;
		var previousCanGoForward = CanGoForward;
		var previousCanGoUp = CanGoUp;
		var columnsApplied = false;
		if (columns is { } pendingColumns && _pane.BrowseSession.Generation == pendingColumns.Generation)
		{
			_detailsColumns = pendingColumns.Columns;
			columnsApplied = true;
		}

		if (state is not null && state.Generation != _pane.BrowseSession.Generation)
		{
			state = null;
		}

		if (state is not null)
		{
			IsLoading = state.IsLoading;
			ErrorMessage = state.ErrorMessage;
			LocationText = state.LocationText;
			_viewSettings = state.ViewSettings;
		}

		if (selection is not null)
		{
			SelectedKeys = selection;
		}

		var flags = BrowseUpdateFlags.None;
		if (appliedChanges.Count is not 0)
		{
			flags |= BrowseUpdateFlags.Items | BrowseUpdateFlags.Status;
		}

		if (columnsApplied)
		{
			flags |= BrowseUpdateFlags.Columns;
		}

		if (!Equals(previousViewSettings, ViewSettings))
		{
			flags |= BrowseUpdateFlags.ViewSettings;
		}

		if (properties.Length is not 0)
		{
			flags |= BrowseUpdateFlags.Presentation;
		}

		if (selection is not null)
		{
			flags |= BrowseUpdateFlags.Selection;
		}

		if (state is not null)
		{
			if (!string.Equals(previousLocationText, LocationText, StringComparison.Ordinal))
			{
				flags |= BrowseUpdateFlags.Location;
			}

			if (previousIsLoading != IsLoading)
			{
				flags |= BrowseUpdateFlags.Loading;
			}

			if (previousCanGoBack != CanGoBack || previousCanGoForward != CanGoForward || previousCanGoUp != CanGoUp)
			{
				flags |= BrowseUpdateFlags.NavigationCapabilities;
			}

			if (!string.Equals(previousErrorMessage, ErrorMessage, StringComparison.Ordinal))
			{
				flags |= BrowseUpdateFlags.Status;
			}
		}

		if (wasBusy != IsBusy)
		{
			flags |= BrowseUpdateFlags.Loading;
		}

		if (flags is not BrowseUpdateFlags.None)
		{
			var updateStartTimestamp = Stopwatch.GetTimestamp();
			Updated?.Invoke(this, new CoreBrowseUpdatedEventArgs(appliedChanges, flags));
			UiDiagnosticLog.Write(
				"BrowsePresentationAdapter",
				$"Updated callback sequence={drainSequence} changes={appliedChanges.Count} flags={flags} " +
				$"callbackMs={Stopwatch.GetElapsedTime(updateStartTimestamp).TotalMilliseconds:F1}");
		}

		foreach (var thumbnail in thumbnails)
		{
			QueueThumbnailApply(thumbnail.Key, thumbnail.Value);
		}

		if (hasPendingItemBatches || HasPendingPresentationUpdates())
		{
			ScheduleDrain(DispatcherQueuePriority.Low);
		}

		var drainElapsed = Stopwatch.GetElapsedTime(drainStartTimestamp);
		UpdateMaximum(ref _diagnosticLongestDrainTicks, drainElapsed.Ticks);
		UiDiagnosticLog.Write(
			"BrowsePresentationAdapter",
			$"Drain END sequence={drainSequence} adapterItems={_items.Count} loading={IsLoading} elapsedMs={drainElapsed.TotalMilliseconds:F1} " +
			$"longestUiMs={TimeSpan.FromTicks(Volatile.Read(ref _diagnosticLongestDrainTicks)).TotalMilliseconds:F1}");
	}

	private PendingItemBatch? TakeNextPendingItemBatchLocked(int maximumItemCount, long currentGeneration, out bool hasPendingItemBatches)
	{
		while (_pendingItemBatches.First is { } staleNode && staleNode.Value.Generation != currentGeneration)
		{
			_pendingItemBatches.RemoveFirst();
		}

		if (_pendingItemBatches.First is not { } node)
		{
			hasPendingItemBatches = false;

			return null;
		}

		_pendingItemBatches.RemoveFirst();
		var changes = TakeChanges(node.Value.Changes, maximumItemCount, out var remainingChanges);
		if (remainingChanges.Count is not 0)
		{
			_pendingItemBatches.AddFirst(new PendingItemBatch(node.Value.Generation, node.Value.PreviousVersion, node.Value.Version, remainingChanges));
		}

		hasPendingItemBatches = _pendingItemBatches.Count is not 0;

		return new PendingItemBatch(node.Value.Generation, node.Value.PreviousVersion, node.Value.Version, changes, remainingChanges.Count is 0);
	}

	private static IReadOnlyList<BrowseItemViewModelChange> TakeChanges(
		IReadOnlyList<BrowseItemViewModelChange> changes,
		int maximumItemCount,
		out IReadOnlyList<BrowseItemViewModelChange> remainingChanges)
	{
		var selected = new List<BrowseItemViewModelChange>();
		var remaining = new List<BrowseItemViewModelChange>();
		var itemCount = 0;
		for (var index = 0; index < changes.Count; index++)
		{
			var change = changes[index];
			var changeItemCount = GetItemCount(change);
			if (itemCount is not 0 && itemCount + changeItemCount > maximumItemCount)
			{
				remaining.AddRange(changes.Skip(index));

				break;
			}

			if (change is BrowseItemViewModelsAdded added && itemCount + changeItemCount > maximumItemCount)
			{
				var takeCount = maximumItemCount - itemCount;
				selected.Add(new BrowseItemViewModelsAdded(added.StartingIndex, added.Items.Take(takeCount).ToArray()));
				remaining.Add(new BrowseItemViewModelsAdded(added.StartingIndex + takeCount, added.Items.Skip(takeCount).ToArray()));
				remaining.AddRange(changes.Skip(index + 1));

				break;
			}

			if (change is BrowseItemViewModelsReset reset && itemCount + changeItemCount > maximumItemCount)
			{
				var takeCount = maximumItemCount - itemCount;
				selected.Add(new BrowseItemViewModelsReset(reset.Items.Take(takeCount).ToArray()));
				remaining.Add(new BrowseItemViewModelsAdded(takeCount, reset.Items.Skip(takeCount).ToArray()));
				remaining.AddRange(changes.Skip(index + 1));

				break;
			}

			selected.Add(change);
			itemCount += changeItemCount;
		}

		remainingChanges = remaining.ToArray();

		return selected.ToArray();
	}

	private static int GetItemCount(BrowseItemViewModelChange change) =>
		change switch
		{
			BrowseItemViewModelsAdded added => added.Items.Count,
			BrowseItemViewModelsReset reset => reset.Items.Count,
			_ => 1,
		};

	private static int GetItemCount(IReadOnlyList<BrowseItemViewModelChange> changes)
	{
		var count = 0;
		foreach (var change in changes)
		{
			count += GetItemCount(change);
		}

		return count;
	}

	private KeyValuePair<StorableKey, IReadOnlyDictionary<string, object?>>[] TakePendingPropertiesLocked()
	{
		var properties = _pendingProperties.Take(MaxItemsPerDrain).ToArray();
		foreach (var property in properties)
		{
			_pendingProperties.Remove(property.Key);
		}

		return properties;
	}

	private KeyValuePair<StorableKey, ThumbnailResult?>[] TakePendingThumbnailsLocked()
	{
		var thumbnails = _pendingThumbnails.Take(MaxThumbnailsPerDrain).ToArray();
		foreach (var thumbnail in thumbnails)
		{
			_pendingThumbnails.Remove(thumbnail.Key);
		}

		return thumbnails;
	}

	private bool HasPendingItemUiWork()
	{
		lock (_pendingLock)
		{
			return _pendingItemBatches.Count is not 0 || Volatile.Read(ref _isApplyingItemBatch) is not 0;
		}
	}

	private bool HasPendingPresentationUpdates()
	{
		lock (_pendingLock)
		{
			return _pendingProperties.Count is not 0 || _pendingThumbnails.Count is not 0;
		}
	}

	private async Task ApplyThumbnailAsync(StorableKey key, ThumbnailResult? thumbnail)
	{
		try
		{
			if (!TryGetItemViewModel(key, out var item))
			{
				RequeueThumbnailIfCurrent(key, thumbnail);

				return;
			}

			var image = thumbnail is null ? null : await DecodeThumbnailAsync(thumbnail).ConfigureAwait(true);
			if (Volatile.Read(ref _isDisposed) is not 0)
			{
				return;
			}

			if (TryGetItemViewModel(key, out item))
			{
				item.SetThumbnail(image);
				if (image is not null)
				{
					var displayCount = Interlocked.Increment(ref _diagnosticThumbnailDisplayCount);
					if (displayCount is 1)
					{
						UiDiagnosticLog.Write("BrowsePresentationAdapter", $"First thumbnail displayed key={key}");
					}
				}

				return;
			}

			RequeueThumbnailIfCurrent(key, thumbnail);
		}
		catch
		{
			// Thumbnail decoding is best effort.
		}
	}

	private void QueueThumbnailApply(StorableKey key, ThumbnailResult? thumbnail)
	{
		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		Task task;
		lock (_thumbnailTaskLock)
		{
			if (Volatile.Read(ref _isDisposed) is not 0)
			{
				return;
			}

			task = ApplyThumbnailAsync(key, thumbnail);
			_thumbnailTasks.Add(task);
		}

		_ = TrackThumbnailTaskAsync(task);
	}

	private async Task TrackThumbnailTaskAsync(Task task)
	{
		try
		{
			await task.ConfigureAwait(false);
		}
		catch
		{
			// Thumbnail updates are best effort.
		}
		finally
		{
			lock (_thumbnailTaskLock)
			{
				_thumbnailTasks.Remove(task);
			}
		}
	}

	private async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage> DecodeThumbnailAsync(ThumbnailResult thumbnail)
	{
		await _thumbnailDecodeGate.WaitAsync(_lifetime.Token).ConfigureAwait(true);
		try
		{
			return await ThumbnailImageFactory.CreateAsync(thumbnail.Content).ConfigureAwait(true);
		}
		finally
		{
			_thumbnailDecodeGate.Release();
		}
	}

	private void RequeueThumbnailIfCurrent(StorableKey key, ThumbnailResult? thumbnail)
	{
		if (!_pane.BrowseSession.Contains(key))
		{
			return;
		}

		lock (_pendingLock)
		{
			if (Volatile.Read(ref _isDisposed) is 0)
			{
				_pendingThumbnails[key] = thumbnail;
			}
		}

		ScheduleDrain();
	}

	private bool TryGetItemViewModel(StorableKey key, out BrowseItemViewModel item)
	{
		lock (_itemsLock)
		{
			return _itemsByKey.TryGetValue(key, out item!);
		}
	}

	private void ApplyProperties(StorableKey key, IReadOnlyDictionary<string, object?> properties)
	{
		if (!TryGetItemViewModel(key, out var item))
		{
			if (!_pane.BrowseSession.Contains(key))
			{
				return;
			}

			lock (_pendingLock)
			{
				if (Volatile.Read(ref _isDisposed) is 0)
				{
					_pendingProperties[key] = properties;
				}
			}

			ScheduleDrain();

			return;
		}

		item.SetProperties(properties);
		Interlocked.Increment(ref _diagnosticPropertyChangeCount);
	}

	private void StartColumnsLoad()
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || Volatile.Read(ref _isApplyingDefaultColumns) is not 0)
		{
			return;
		}

		var session = _pane.BrowseSession;
		if (session.Context is not FolderBrowseLocationContext context || session.Generation is 0)
		{
			CancelColumnsLoad();
			QueueColumns(session.Generation, CreateFallbackColumns());

			return;
		}

		var hasCachedColumns = false;
		WindowsShellColumnSet? cachedColumnSet = null;
		ColumnLoad? previousLoad = null;
		ColumnLoad? loadToStart = null;
		Task previousTask = Task.CompletedTask;
		lock (_pendingLock)
		{
			if (_columnCache is { } cache && cache.Generation == session.Generation && ReferenceEquals(cache.Context, context))
			{
				hasCachedColumns = true;
				cachedColumnSet = cache.ColumnSet;
			}
			else if (_columnLoad is { } activeLoad && activeLoad.Generation == session.Generation && ReferenceEquals(activeLoad.Context, context))
			{
				return;
			}
			else
			{
				previousLoad = _columnLoad;
				previousTask = previousLoad?.Task ?? Task.CompletedTask;
				loadToStart = new ColumnLoad(context, session.Generation, _lifetime.Token);
				_columnLoad = loadToStart;
				_columnCache = null;
			}
		}

		previousLoad?.Cancel();
		if (loadToStart is { } load)
		{
			load.Start(() => LoadColumnsAsync(load, previousTask));
		}

		if (hasCachedColumns)
		{
			QueueColumns(session.Generation, cachedColumnSet is null ? CreateFallbackColumns() : CreateDetailsColumns(cachedColumnSet, session.ViewSettings));
		}
	}

	private async Task LoadColumnsAsync(ColumnLoad load, Task previousTask)
	{
		try
		{
			await previousTask.ConfigureAwait(false);
			load.Token.ThrowIfCancellationRequested();

			var columnSet = await load.Context.GetColumnsAsync(load.Token).ConfigureAwait(false);
			if (!IsCurrentColumnsContext(load.Context, load.Generation, load.Token))
			{
				return;
			}

			lock (_pendingLock)
			{
				if (IsCurrentColumnsContext(load.Context, load.Generation, load.Token))
				{
					_columnCache = new ColumnCache(load.Context, load.Generation, columnSet);
				}
			}

			if (columnSet is null)
			{
				QueueColumns(load.Generation, CreateFallbackColumns());

				return;
			}

			var session = _pane.BrowseSession;
			var settings = session.ViewSettings;
			if (settings.Columns.Count is 0)
			{
				var defaultColumns = CreateDefaultViewColumnSettings(columnSet);
				if (defaultColumns.Count is not 0)
				{
					Interlocked.Exchange(ref _isApplyingDefaultColumns, 1);
					try
					{
						var nextSettings = new BrowseViewSettings(
							settings.LayoutMode,
							defaultColumns,
							settings.SortPropertyId,
							settings.SortDirection,
							settings.ItemSize,
							settings.GroupPropertyId,
							settings.GroupDirection);
						await session.UpdateViewSettingsAsync(nextSettings, load.Token).ConfigureAwait(false);
					}
					finally
					{
						Interlocked.Exchange(ref _isApplyingDefaultColumns, 0);
					}

					if (!IsCurrentColumnsContext(load.Context, load.Generation, load.Token))
					{
						return;
					}

					settings = session.ViewSettings;
				}
			}

			QueueColumns(load.Generation, CreateDetailsColumns(columnSet, settings));
		}
		catch (OperationCanceledException) when (load.IsCancellationRequested)
		{
		}
		catch
		{
			if (IsCurrentColumnsContext(load.Context, load.Generation, CancellationToken.None))
			{
				QueueColumns(load.Generation, CreateFallbackColumns());
			}
		}
		finally
		{
			lock (_pendingLock)
			{
				if (ReferenceEquals(_columnLoad, load))
				{
					_columnLoad = null;
				}
			}

			load.Dispose();
		}
	}

	private void CancelColumnsLoad()
	{
		ColumnLoad? load;
		lock (_pendingLock)
		{
			load = _columnLoad;
			_columnCache = null;
		}

		load?.Cancel();
	}

	private bool IsCurrentColumnsContext(FolderBrowseLocationContext context, long generation, CancellationToken cancellationToken)
	{
		return !cancellationToken.IsCancellationRequested &&
			_pane.BrowseSession.Generation == generation &&
			ReferenceEquals(_pane.BrowseSession.Context, context) &&
			Volatile.Read(ref _isDisposed) is 0;
	}

	private void QueueColumns(long generation, IReadOnlyList<DetailsColumnViewModel> columns)
	{
		lock (_pendingLock)
		{
			if (Volatile.Read(ref _isDisposed) is not 0)
			{
				return;
			}

			_pendingColumns = new PendingColumns(generation, columns);
		}

		ScheduleDrain();
	}

	private static IReadOnlyList<ViewColumnSettings> CreateDefaultViewColumnSettings(WindowsShellColumnSet columnSet)
	{
		var settings = new List<ViewColumnSettings>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var column in columnSet.DefaultVisible)
		{
			if (seen.Add(column.PropertyId))
			{
				settings.Add(new ViewColumnSettings(column.PropertyId, GetDefaultColumnWidth(column), settings.Count));
			}
		}

		return Array.AsReadOnly(settings.ToArray());
	}

	private static IReadOnlyList<DetailsColumnViewModel> CreateDetailsColumns(WindowsShellColumnSet columnSet, BrowseViewSettings settings)
	{
		var availableColumns = columnSet.All
			.Where(static column => !column.IsHidden && !column.IsSecondaryUi)
			.GroupBy(static column => column.PropertyId, StringComparer.Ordinal)
			.ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
		var selectedColumns = new List<WindowsShellColumn>();
		var selectedPropertyIds = new HashSet<string>(StringComparer.Ordinal);

		foreach (var configuredColumn in settings.Columns.Where(static column => column.IsVisible).OrderBy(static column => column.Order))
		{
			if (availableColumns.TryGetValue(configuredColumn.PropertyId, out var column) && selectedPropertyIds.Add(column.PropertyId))
			{
				selectedColumns.Add(column);
			}
		}

		if (selectedColumns.Count is 0)
		{
			foreach (var column in columnSet.DefaultVisible)
			{
				if (availableColumns.ContainsKey(column.PropertyId) && selectedPropertyIds.Add(column.PropertyId))
				{
					selectedColumns.Add(column);
				}
			}
		}

		if (selectedColumns.Count is 0)
		{
			foreach (var column in availableColumns.Values.OrderBy(static column => column.Index))
			{
				if (selectedPropertyIds.Add(column.PropertyId))
				{
					selectedColumns.Add(column);
				}
			}
		}

		if (selectedColumns.Count is 0)
		{
			return CreateFallbackColumns();
		}

		var result = new List<DetailsColumnViewModel>(selectedColumns.Count);
		foreach (var column in selectedColumns)
		{
			var configuredColumn = settings.Columns.FirstOrDefault(setting => setting.PropertyId.Equals(column.PropertyId, StringComparison.Ordinal));
			var width = configuredColumn?.Width ?? GetDefaultColumnWidth(column);
			var isNameColumn = column.PropertyId.Equals("System.ItemNameDisplay", StringComparison.Ordinal) || column.PropertyId.Equals("name", StringComparison.OrdinalIgnoreCase);
			result.Add(new DetailsColumnViewModel(column.PropertyId, column.DisplayName, width, column.Alignment, isNameColumn, !column.IsFixedWidth, canGroup: column.CanGroup));
		}

		return Array.AsReadOnly(result.ToArray());
	}

	private static double GetDefaultColumnWidth(WindowsShellColumn column)
	{
		var width = column.HeaderWidthCharacters is > 0
			? column.HeaderWidthCharacters * 8d
			: 120d;

		return Math.Clamp(width, 72d, 320d);
	}

	private static IReadOnlyList<DetailsColumnViewModel> CreateFallbackColumns()
	{
		return Array.AsReadOnly(new[]
		{
			new DetailsColumnViewModel("System.ItemNameDisplay", "System.ItemNameDisplay", 180, WindowsShellColumnAlignment.Left, isPrimary: true),
			new DetailsColumnViewModel("System.ItemTypeText", "System.ItemTypeText", 100, WindowsShellColumnAlignment.Left),
			new DetailsColumnViewModel("reference", "reference", 220, WindowsShellColumnAlignment.Left),
		});
	}

	private bool TryApplyChanges(IReadOnlyList<BrowseItemViewModelChange> changes, ICollection<BrowseItemViewModelChange> appliedChanges)
	{
		lock (_itemsLock)
		{
			var changeIndex = 0;
			while (changeIndex < changes.Count)
			{
				if (changes[changeIndex] is BrowseItemViewModelAdded firstAdded && firstAdded.Index >= 0 && firstAdded.Index <= _items.Count)
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
						_items.InsertRange(firstAdded.Index, addedItems);
						foreach (var addedItem in addedItems)
						{
							_itemsByKey.Add(addedItem.Reference.GetKey(), addedItem);
						}

						for (var index = changeIndex; index < nextChangeIndex; index++)
						{
							appliedChanges.Add(changes[index]);
						}

						changeIndex = nextChangeIndex;

						continue;
					}
				}

				var change = changes[changeIndex];
				switch (change)
				{
					case BrowseItemViewModelsAdded addedRange
						when addedRange.StartingIndex >= 0 && addedRange.StartingIndex <= _items.Count:
						_items.InsertRange(addedRange.StartingIndex, addedRange.Items);
						foreach (var addedItem in addedRange.Items)
						{
							_itemsByKey.Add(addedItem.Reference.GetKey(), addedItem);
						}
						break;
					case BrowseItemViewModelAdded added
						when added.Index >= 0 && added.Index <= _items.Count:
						_items.Insert(added.Index, added.Item);
						_itemsByKey.Add(added.Item.Reference.GetKey(), added.Item);
						break;
					case BrowseItemViewModelRemoved removed
						when removed.Index >= 0 && removed.Index < _items.Count:
						var removedItem = _items[removed.Index];
						_items.RemoveAt(removed.Index);
						_itemsByKey.Remove(removedItem.Reference.GetKey());
						break;
					case BrowseItemViewModelReplaced replaced
						when replaced.Index >= 0 && replaced.Index < _items.Count:
						var previousItem = _items[replaced.Index];
						_items[replaced.Index] = replaced.Item;
						_itemsByKey.Remove(previousItem.Reference.GetKey());
						_itemsByKey.Add(replaced.Item.Reference.GetKey(), replaced.Item);
						break;
					case BrowseItemViewModelMoved moved
						when moved.PreviousIndex >= 0
							&& moved.PreviousIndex < _items.Count
							&& moved.CurrentIndex >= 0
							&& moved.CurrentIndex < _items.Count:
						var item = _items[moved.PreviousIndex];
						_items.RemoveAt(moved.PreviousIndex);
						_items.Insert(moved.CurrentIndex, item);
						break;
					case BrowseItemViewModelsReset reset:
						_items.Clear();
						_itemsByKey.Clear();
						_items.AddRange(reset.Items);
						foreach (var resetItem in reset.Items)
						{
							UpdateMaterializedItem(resetItem);
							_itemsByKey.Add(resetItem.Reference.GetKey(), resetItem);
						}
						break;
					default:

						return false;
				}

				appliedChanges.Add(change);
				changeIndex++;
			}

			return true;
		}
	}

	private void ResetFromCurrentSession(ICollection<BrowseItemViewModelChange> appliedChanges)
	{
		var session = _pane.BrowseSession;
		long generation;
		long itemsVersion;
		IReadOnlyList<IStorableModel> items;
		do
		{
			generation = session.Generation;
			itemsVersion = session.ItemsVersion;
			items = session.Items;
		}
		while (generation != session.Generation || itemsVersion != session.ItemsVersion);

		var resetChanges = ProjectReset(items);
		if (generation != session.Generation)
		{
			_appliedItemsVersion = -1;
			appliedChanges.Clear();

			return;
		}

		var reset = resetChanges[0] as BrowseItemViewModelsReset
			?? throw new InvalidOperationException("A projected session reset must start with a reset change.");
		lock (_itemsLock)
		{
			_items.Clear();
			_itemsByKey.Clear();
			_items.AddRange(reset.Items);
			foreach (var resetItem in reset.Items)
			{
				UpdateMaterializedItem(resetItem);
				_itemsByKey.Add(resetItem.Reference.GetKey(), resetItem);
			}
		}
		_appliedItemsVersion = -1;
		appliedChanges.Clear();
		appliedChanges.Add(reset);
		var remainingChanges = resetChanges.Skip(1).ToArray();
		lock (_pendingLock)
		{
			var node = _pendingItemBatches.First;
			while (node is not null)
			{
				var next = node.Next;
				if (node.Value.Generation < generation || (node.Value.Generation == generation && node.Value.Version <= itemsVersion))
				{
					_pendingItemBatches.Remove(node);
				}

				node = next;
			}

			if (remainingChanges.Length is not 0)
			{
				_pendingItemBatches.AddFirst(new PendingItemBatch(generation, -1, itemsVersion, remainingChanges));
			}
			else
			{
				_appliedItemsVersion = itemsVersion;
			}
		}
	}

	private IReadOnlyList<BrowseItemViewModelChange> ProjectChanges(IReadOnlyList<BrowseItemChange> changes)
	{
		var projectedChanges = new List<BrowseItemViewModelChange>();
		foreach (var change in changes)
		{
			if (change is BrowseItemsReset reset)
			{
				projectedChanges.AddRange(ProjectReset(reset.Items));
			}
			else
			{
				projectedChanges.Add(ProjectChange(change));
			}
		}

		return projectedChanges;
	}

	private IReadOnlyList<BrowseItemViewModelChange> ProjectReset(IReadOnlyList<IStorableModel> items)
	{
		var viewModels = items.Select(GetOrCreateItemViewModel).ToArray();
		if (viewModels.Length <= MaxItemsPerDrain)
		{
			return [new BrowseItemViewModelsReset(viewModels)];
		}

		var projectedChanges = new List<BrowseItemViewModelChange>
		{
			new BrowseItemViewModelsReset(viewModels.Take(MaxItemsPerDrain).ToArray()),
		};
		for (var startingIndex = MaxItemsPerDrain; startingIndex < viewModels.Length; startingIndex += MaxItemsPerDrain)
		{
			projectedChanges.Add(new BrowseItemViewModelsAdded(startingIndex, viewModels.Skip(startingIndex).Take(MaxItemsPerDrain).ToArray()));
		}

		return projectedChanges;
	}

	private BrowseItemViewModel GetOrCreateItemViewModel(IStorableModel item)
	{
		lock (_itemsLock)
		{
			if (_itemsByKey.TryGetValue(item.Reference.GetKey(), out var existing))
			{
				return existing;
			}

			return CreateItemViewModel(item);
		}
	}

	private void UpdateMaterializedItem(BrowseItemViewModel item)
	{
		if (_pane.BrowseSession.TryGet(item.Reference.GetKey(), out var model))
		{
			item.UpdateModel(model, _pane.BrowseSession.DisplaySettings.ShowFileExtensions);
			ApplyPresentation(item, item.Reference.GetKey());
		}
	}

	private BrowseItemViewModelChange ProjectChange(BrowseItemChange change) =>
		change switch
		{
			BrowseItemAdded added => new BrowseItemViewModelAdded(added.Index, CreateItemViewModel(added.Item)),
			BrowseItemsAdded added => new BrowseItemViewModelsAdded(added.StartingIndex, added.Items.Select(CreateItemViewModel).ToArray()),
			BrowseItemRemoved removed => new BrowseItemViewModelRemoved(removed.Index),
			BrowseItemReplaced replaced => new BrowseItemViewModelReplaced(replaced.Index, CreateItemViewModel(replaced.NewItem)),
			BrowseItemMoved moved => new BrowseItemViewModelMoved(moved.PreviousIndex, moved.CurrentIndex),
			_ => throw new InvalidOperationException($"Unsupported Core browse item change '{change.GetType().Name}'."),
		};

	private BrowseItemViewModel CreateItemViewModel(IStorableModel item)
	{
		var viewModel = new BrowseItemViewModel(item.Name, item is IFolderModel, item.Reference, item.IsHidden, _pane.BrowseSession.DisplaySettings.ShowFileExtensions);
		var itemViewModelCount = Interlocked.Increment(ref _diagnosticItemViewModelCount);
		if (itemViewModelCount is 1)
		{
			UiDiagnosticLog.Write("BrowsePresentationAdapter", $"First item view model created key={item.Reference.GetKey()}");
		}
		ApplyPresentation(viewModel, item.Reference.GetKey());

		return viewModel;
	}

	private void ApplyPresentation(BrowseItemViewModel viewModel, StorableKey key)
	{
		if (_pane.BrowseSession.TryGetPresentation(key, out var presentation))
		{
			if (presentation.Properties.Count is not 0)
			{
				viewModel.SetProperties(presentation.Properties);
			}
			else
			{
				viewModel.SetProperties(BrowseItemViewModel.EmptyProperties);
			}

			if (presentation.Thumbnail is { } thumbnail)
			{
				QueuePendingThumbnail(key, thumbnail);
			}
			else
			{
				viewModel.SetThumbnail(null);
			}
		}
		else
		{
			viewModel.SetProperties(BrowseItemViewModel.EmptyProperties);
			viewModel.SetThumbnail(null);
		}
	}

	private void QueuePendingThumbnail(StorableKey key, ThumbnailResult thumbnail)
	{
		lock (_pendingLock)
		{
			if (Volatile.Read(ref _isDisposed) is 0)
			{
				_pendingThumbnails[key] = thumbnail;
			}
		}
	}

	private CancellationTokenSource CreateLinkedCancellation(CancellationToken cancellationToken) =>
		CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

	internal Task NavigateToLocationAsync(BrowseLocation location, CancellationToken cancellationToken)
	{
		EnsureActive();

		cancellationToken.ThrowIfCancellationRequested();

		LocationNavigation navigation;
		lock (_locationNavigationLock)
		{
			if (_locationNavigation is { } activeNavigation && Equals(activeNavigation.Location, location))
			{
				return WaitForLocationNavigationAsync(activeNavigation, cancellationToken);
			}

			if (_locationNavigation is null && _pane.BrowseSession.Error is null && Equals(_pane.BrowseSession.Location, location))
			{
				return Task.CompletedTask;
			}

			var task = _pane.NavigateAsync(location, cancellationToken: _lifetime.Token).AsTask();
			navigation = new LocationNavigation(location, task);
			_locationNavigation = navigation;
		}

		_ = TrackLocationNavigationAsync(navigation);

		return WaitForLocationNavigationAsync(navigation, cancellationToken);
	}

	private static Task WaitForLocationNavigationAsync(LocationNavigation navigation, CancellationToken cancellationToken)
	{
		return cancellationToken.CanBeCanceled ? navigation.Task.WaitAsync(cancellationToken) : navigation.Task;
	}

	private static async Task ObserveBackgroundTaskAsync(Task? task)
	{
		if (task is null)
		{
			return;
		}

		try
		{
			await task.ConfigureAwait(false);
		}
		catch
		{
			// The initiating caller observes operation failures.
		}
	}

	private async Task TrackLocationNavigationAsync(LocationNavigation navigation)
	{
		try
		{
			await navigation.Task.ConfigureAwait(false);
		}
		catch
		{
			// Callers observe the shared navigation task.
		}
		finally
		{
			lock (_locationNavigationLock)
			{
				if (ReferenceEquals(_locationNavigation, navigation))
				{
					_locationNavigation = null;
				}
			}
		}
	}

	private string GetLocationText(BrowseLocation? location)
	{
		return location switch
		{
			HomeLocation => _text.Home,
			FolderLocation folder when folder.Folder.LastKnownAddress is
				{ Scheme: var scheme, Value: var value }
				&& string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase)
				=> value,
			FolderLocation folder => folder.Folder.LastKnownAddress?.ToString()
				?? folder.Folder.ItemId,
			_ => location?.GetType().Name ?? _text.Home,
		};
	}

	private void EnsureActive() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

	private static void UpdateMaximum(ref long target, long candidate)
	{
		var current = Volatile.Read(ref target);
		while (candidate > current)
		{
			var previous = Interlocked.CompareExchange(ref target, candidate, current);
			if (previous == current)
			{
				return;
			}

			current = previous;
		}
	}

	private sealed record PendingItemBatch(long Generation, long PreviousVersion, long Version, IReadOnlyList<BrowseItemViewModelChange> Changes, bool IsComplete = true);

	private sealed record PendingColumns(long Generation, IReadOnlyList<DetailsColumnViewModel> Columns);

	private sealed record PendingState(long Generation, bool IsLoading, string? ErrorMessage, string LocationText, BrowseViewSettings ViewSettings);

	private sealed record LocationNavigation(BrowseLocation Location, Task Task);

	private sealed record ColumnCache(FolderBrowseLocationContext Context, long Generation, WindowsShellColumnSet? ColumnSet);

	private sealed class ColumnLoad : IDisposable
	{
		private readonly CancellationTokenSource _cancellation;
		private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public FolderBrowseLocationContext Context { get; }

		public long Generation { get; }

		public CancellationToken Token => _cancellation.Token;

		public bool IsCancellationRequested => _cancellation.IsCancellationRequested;

		public Task Task => _completion.Task;

		public ColumnLoad(FolderBrowseLocationContext context, long generation, CancellationToken lifetimeToken)
		{
			Context = context;
			Generation = generation;
			_cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
		}

		public void Start(Func<Task> action)
		{
			ArgumentNullException.ThrowIfNull(action);

			_ = RunAsync(action);
		}

		public void Cancel()
		{
			try
			{
				_cancellation.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		public void Dispose()
		{
			_cancellation.Dispose();
		}

		private async Task RunAsync(Func<Task> action)
		{
			try
			{
				await action().ConfigureAwait(false);
				_completion.TrySetResult(true);
			}
			catch (OperationCanceledException exception)
			{
				_completion.TrySetCanceled(exception.CancellationToken);
			}
			catch (Exception exception)
			{
				_completion.TrySetException(exception);
			}
		}
	}
}
