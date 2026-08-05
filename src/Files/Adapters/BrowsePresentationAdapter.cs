// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.Globalization;
using Files.Infrastructure;
using Files.Localization;
using Files.ViewModels;
using Files.Core.Sessions;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Files.Core.ViewSettings;

namespace Files.Adapters;

internal sealed class BrowsePresentationAdapter : IDisposable, IAsyncDisposable
{
	private const int MaxItemsPerDrain = 128;
	private const int MaxThumbnailsPerDrain = 8;

	private readonly BrowsePaneSession _pane;
	private readonly IStorageWorkspace _workspace;
	private readonly IUIDispatcher _dispatcher;
	private readonly IBrowsePrefetchCoordinator _prefetch;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Lock _pendingLock = new();
	private readonly Queue<PendingItemBatch> _pendingItemBatches = new();
	private readonly Dictionary<StorableKey, ThumbnailResult?> _pendingThumbnails = [];
	private readonly Dictionary<StorableKey, IReadOnlyDictionary<string, object?>> _pendingProperties = [];
	private readonly List<BrowseItemViewModel> _items = [];
	private readonly Dictionary<StorableKey, BrowseItemViewModel> _itemsByKey = [];
	private PendingState? _pendingState;
	private PendingColumns? _pendingColumns;
	private IReadOnlyList<StorableKey>? _pendingSelection;
	private CancellationTokenSource? _columnsCancellation;
	private IReadOnlyList<DetailsColumnViewModel> _detailsColumns = CreateFallbackColumns();
	private long _appliedItemsVersion = -1;
	private int _diagnosticDrainSequence;
	private bool _drainQueued;
	private int _isApplyingDefaultColumns;
	private int _isDisposed;

	public BrowsePresentationAdapter(BrowsePaneSession pane, IStorageWorkspace workspace, IUIDispatcher dispatcher)
	{
		ArgumentNullException.ThrowIfNull(pane);
		ArgumentNullException.ThrowIfNull(workspace);
		ArgumentNullException.ThrowIfNull(dispatcher);

		_pane = pane;
		_workspace = workspace;
		_dispatcher = dispatcher;
		_prefetch = new BrowsePrefetchCoordinator(_pane.BrowseSession);

		SelectedKeys = Array.Empty<StorableKey>();
		_pane.NavigationStateChanged += Pane_StateChanged;
		_pane.BrowseSession.ItemsChanged += BrowseSession_ItemsChanged;
		_pane.BrowseSession.ItemPresentationChanged +=
			BrowseSession_ItemPresentationChanged;
		_pane.BrowseSession.SelectionChanged += BrowseSession_SelectionChanged;
		QueueInitialSnapshot();
		UiDiagnosticLog.Write("BrowsePresentationAdapter", $"created items={_items.Count} loading={IsLoading}");
	}

	public IReadOnlyList<StorableKey> SelectedKeys { get; private set; }

	public string LocationText { get; private set; } = Strings.Home.GetLocalized();

	public string? ErrorMessage { get; private set; }

	public bool IsLoading { get; private set; }

	public bool CanGoBack => _pane.CanGoBack;

	public bool CanGoForward => _pane.CanGoForward;

	public bool CanGoUp => _pane.CanGoUp;

	public ViewLayoutMode LayoutMode => _pane.BrowseSession.ViewSettings.LayoutMode;

	public string StatusText =>
		ErrorMessage
		?? (IsLoading
			? Strings.Loading.GetLocalized()
			: string.Format(CultureInfo.CurrentCulture, _items.Count is 1 ? Strings.ItemCountSingle.GetLocalized() : Strings.ItemCountPlural.GetLocalized(), _items.Count));

	public event EventHandler<CoreBrowseUpdatedEventArgs>? Updated;

	public IReadOnlyList<BrowseItemViewModel> Items => _items;

	public IReadOnlyList<DetailsColumnViewModel> DetailsColumns => _detailsColumns;

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("BrowsePresentationAdapter", "Initialize START");
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		try
		{
			await _pane.NavigateAsync(HomeLocation.Instance, cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
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

		if (string.Equals(path, Strings.Home.GetLocalized(), StringComparison.OrdinalIgnoreCase) || string.Equals(path, "Home", StringComparison.OrdinalIgnoreCase))
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
					throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Strings.NotFolderFormat.GetLocalized(), path));
				}

				await _pane.NavigateAsync(new FolderLocation(model.Reference), cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
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
			await _pane.NavigateAsync(new FolderLocation(reference), cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
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
			currentSettings.ItemSize);

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
		DisposeAsync().AsTask().GetAwaiter().GetResult();
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
		_columnsCancellation?.Cancel();
		_lifetime.Cancel();
		_lifetime.Dispose();
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
			_pendingState = new PendingState(session.IsLoading, session.Error?.Message, GetLocationText(session.Location));
		}

		StartColumnsLoad();
		ScheduleDrain();
	}

	private void BrowseSession_ItemsChanged(object? sender, BrowseItemsChangedEventArgs args)
	{
		var projectionStartTimestamp = Stopwatch.GetTimestamp();
		var changes = args.Changes.Select(ProjectChange).ToArray();
		UiDiagnosticLog.Write(
			"BrowsePresentationAdapter",
			$"ItemsChanged version={args.Version} previous={args.PreviousVersion} changes={args.Changes.Count} projected={changes.Length} " +
			$"projectionMs={Stopwatch.GetElapsedTime(projectionStartTimestamp).TotalMilliseconds:F1} coreItems={_pane.BrowseSession.Items.Count}");
		lock (_pendingLock)
		{
			_pendingItemBatches.Enqueue(new PendingItemBatch(args.PreviousVersion, args.Version, changes));
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
			$"ItemPresentationChanged key={args.Key} hasProperties={args.Presentation.Properties.Count is not 0} " +
			$"hasThumbnail={args.Presentation.Thumbnail is not null}");
		lock (_pendingLock)
		{
			if (args.Presentation.Properties.Count is not 0)
			{
				_pendingProperties[args.Key] = args.Presentation.Properties;
			}

			_pendingThumbnails[args.Key] = args.Presentation.Thumbnail;
		}

		ScheduleDrain();
	}

	private void QueueInitialSnapshot()
	{
		var session = _pane.BrowseSession;
		var reset = new BrowseItemViewModelsReset(session.Items.Select(CreateItemViewModel).ToArray());
		UiDiagnosticLog.Write("BrowsePresentationAdapter", $"QueueInitialSnapshot items={reset.Items.Count} version={session.ItemsVersion}");
		lock (_pendingLock)
		{
			_pendingItemBatches.Enqueue(new PendingItemBatch(-1, session.ItemsVersion, [reset]));
			_pendingState = new PendingState(session.IsLoading, session.Error?.Message, GetLocationText(session.Location));
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

					_pendingThumbnails[key] = presentation.Thumbnail;
				}
			}
		}

		ScheduleDrain();
	}

	private void ScheduleDrain()
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

		if (!_dispatcher.TryEnqueue(DrainPendingUpdates))
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
			UiDiagnosticLog.Write("BrowsePresentationAdapter", $"Drain queued pendingBatches={pendingBatchCount} _pendingThumbnails={pendingThumbnailCount}");
		}
	}

	private void DrainPendingUpdates()
	{
		var drainStartTimestamp = Stopwatch.GetTimestamp();
		var drainSequence = Interlocked.Increment(ref _diagnosticDrainSequence);
		PendingItemBatch[] itemBatches;
		bool hasPendingItemBatches;
		PendingState? state;
		PendingColumns? columns;
		IReadOnlyList<StorableKey>? selection;
		KeyValuePair<StorableKey, IReadOnlyDictionary<string, object?>>[] properties;
		KeyValuePair<StorableKey, ThumbnailResult?>[] thumbnails;
		bool hasPendingThumbnails;
		lock (_pendingLock)
		{
			if (Volatile.Read(ref _isDisposed) is not 0)
			{
				return;
			}

			itemBatches = TakePendingItemBatchesLocked(out hasPendingItemBatches);
			state = _pendingState;
			_pendingState = null;
			columns = _pendingColumns;
			_pendingColumns = null;
			selection = _pendingSelection;
			_pendingSelection = null;
			properties = _pendingProperties.ToArray();
			_pendingProperties.Clear();
			thumbnails = TakePendingThumbnailsLocked(out hasPendingThumbnails);
			_drainQueued = false;
		}
		var pendingItemCount = itemBatches.Sum(batch => GetItemCount(batch.Changes));
		UiDiagnosticLog.Write(
			"BrowsePresentationAdapter",
			$"Drain START sequence={drainSequence} batches={itemBatches.Length} items={pendingItemCount} thumbnails={thumbnails.Length} hasPending={hasPendingItemBatches}");

		var appliedChanges = new List<BrowseItemViewModelChange>();
		foreach (var batch in itemBatches)
		{
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

			_appliedItemsVersion = batch.Version;
		}

		foreach (var property in properties)
		{
			ApplyProperties(property.Key, property.Value);
		}

		if (columns is { } pendingColumns && _pane.BrowseSession.Generation == pendingColumns.Generation)
		{
			_detailsColumns = pendingColumns.Columns;
			foreach (var item in _items)
			{
				item.SetDetailsColumns(_detailsColumns);
			}
		}

		if (state is not null)
		{
			IsLoading = state.IsLoading;
			ErrorMessage = state.ErrorMessage;
			LocationText = state.LocationText;
		}

		if (selection is not null)
		{
			SelectedKeys = selection;
		}

		if (appliedChanges.Count > 0 || state is not null || columns is not null || selection is not null)
		{
			var updateStartTimestamp = Stopwatch.GetTimestamp();
			Updated?.Invoke(this, new CoreBrowseUpdatedEventArgs(appliedChanges, selection is not null));
			UiDiagnosticLog.Write(
				"BrowsePresentationAdapter",
				$"Updated callback sequence={drainSequence} changes={appliedChanges.Count} selectionChanged={selection is not null} " +
				$"callbackMs={Stopwatch.GetElapsedTime(updateStartTimestamp).TotalMilliseconds:F1}");
		}

		foreach (var thumbnail in thumbnails)
		{
			_ = ApplyThumbnailAsync(thumbnail.Key, thumbnail.Value);
		}

		if (hasPendingItemBatches || hasPendingThumbnails || HasPendingThumbnails())
		{
			ScheduleDrain();
		}

		UiDiagnosticLog.Write(
			"BrowsePresentationAdapter",
			$"Drain END sequence={drainSequence} adapterItems={_items.Count} loading={IsLoading} elapsedMs={Stopwatch.GetElapsedTime(drainStartTimestamp).TotalMilliseconds:F1}");
	}

	private PendingItemBatch[] TakePendingItemBatchesLocked(out bool hasPendingItemBatches)
	{
		var batches = new List<PendingItemBatch>();
		var itemCount = 0;
		while (_pendingItemBatches.Count is not 0)
		{
			var batch = _pendingItemBatches.Peek();
			var batchItemCount = GetItemCount(batch.Changes);
			if (batches.Count is not 0 && itemCount + batchItemCount > MaxItemsPerDrain)
			{
				break;
			}

			_pendingItemBatches.Dequeue();
			batches.Add(batch);
			itemCount += batchItemCount;
		}

		hasPendingItemBatches = _pendingItemBatches.Count is not 0;

		return batches.ToArray();
	}

	private static int GetItemCount(IReadOnlyList<BrowseItemViewModelChange> changes)
	{
		var count = 0;
		foreach (var change in changes)
		{
			count += change switch
			{
				BrowseItemViewModelsReset reset => reset.Items.Count,
				_ => 1,
			};
		}

		return count;
	}

	private KeyValuePair<StorableKey, ThumbnailResult?>[] TakePendingThumbnailsLocked(out bool hasPendingThumbnails)
	{
		var thumbnails = _pendingThumbnails.Take(MaxThumbnailsPerDrain).ToArray();
		foreach (var thumbnail in thumbnails)
		{
			_pendingThumbnails.Remove(thumbnail.Key);
		}

		hasPendingThumbnails = _pendingThumbnails.Count is not 0;

		return thumbnails;
	}

	private bool HasPendingThumbnails()
	{
		lock (_pendingLock)
		{
			return _pendingThumbnails.Count is not 0;
		}
	}

	private async Task ApplyThumbnailAsync(StorableKey key, ThumbnailResult? thumbnail)
	{
		try
		{
			if (!_itemsByKey.TryGetValue(key, out var item))
			{
				RequeueThumbnailIfCurrent(key, thumbnail);

				return;
			}

			var image = thumbnail is null
				? null
				: await ThumbnailImageFactory
					.CreateAsync(thumbnail.Content)
					.ConfigureAwait(true);
			if (Volatile.Read(ref _isDisposed) is not 0)
			{
				return;
			}

			if (_itemsByKey.TryGetValue(key, out item))
			{
				item.SetThumbnail(image);

				return;
			}

			RequeueThumbnailIfCurrent(key, thumbnail);
		}
		catch
		{
			// Thumbnail decoding is best effort.
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

	private void ApplyProperties(StorableKey key, IReadOnlyDictionary<string, object?> properties)
	{
		if (!_itemsByKey.TryGetValue(key, out var item))
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
			QueueColumns(session.Generation, CreateFallbackColumns());

			return;
		}

		CancellationTokenSource cancellation;
		lock (_pendingLock)
		{
			_columnsCancellation?.Cancel();
			cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
			_columnsCancellation = cancellation;
		}

		_ = LoadColumnsAsync(context, session.Generation, cancellation);
	}

	private async Task LoadColumnsAsync(FolderBrowseLocationContext context, long generation, CancellationTokenSource cancellation)
	{
		try
		{
			var columnSet = await context.GetColumnsAsync(cancellation.Token).ConfigureAwait(false);
			if (columnSet is null || !IsCurrentColumnsContext(context, generation, cancellation.Token))
			{
				if (columnSet is null && IsCurrentColumnsContext(context, generation, cancellation.Token))
				{
					QueueColumns(generation, CreateFallbackColumns());
				}

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
						var nextSettings = new BrowseViewSettings(settings.LayoutMode, defaultColumns, settings.SortPropertyId, settings.SortDirection, settings.ItemSize);
						await session.UpdateViewSettingsAsync(nextSettings, cancellation.Token).ConfigureAwait(false);
					}
					finally
					{
						Interlocked.Exchange(ref _isApplyingDefaultColumns, 0);
					}

					if (!IsCurrentColumnsContext(context, generation, cancellation.Token))
					{
						return;
					}

					settings = session.ViewSettings;
				}
			}

			QueueColumns(generation, CreateDetailsColumns(columnSet, settings));
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch
		{
			if (IsCurrentColumnsContext(context, generation, CancellationToken.None))
			{
				QueueColumns(generation, CreateFallbackColumns());
			}
		}
		finally
		{
			lock (_pendingLock)
			{
				if (ReferenceEquals(_columnsCancellation, cancellation))
				{
					_columnsCancellation = null;
				}
			}

			cancellation.Dispose();
		}
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
			result.Add(new DetailsColumnViewModel(column.PropertyId, column.DisplayName, width, column.Alignment, isNameColumn));
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
			new DetailsColumnViewModel("System.ItemNameDisplay", "System.ItemNameDisplay", 180, WindowsShellColumnAlignment.Left, isStretch: true),
			new DetailsColumnViewModel("System.ItemTypeText", "System.ItemTypeText", 100, WindowsShellColumnAlignment.Left),
			new DetailsColumnViewModel("reference", "reference", 220, WindowsShellColumnAlignment.Left),
		});
	}

	private bool TryApplyChanges(IReadOnlyList<BrowseItemViewModelChange> changes, ICollection<BrowseItemViewModelChange> appliedChanges)
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

	private void ResetFromCurrentSession(ICollection<BrowseItemViewModelChange> appliedChanges)
	{
		var session = _pane.BrowseSession;
		var reset = new BrowseItemViewModelsReset(session.Items.Select(CreateItemViewModel).ToArray());
		_items.Clear();
		_itemsByKey.Clear();
		_items.AddRange(reset.Items);
		foreach (var resetItem in reset.Items)
		{
			_itemsByKey.Add(resetItem.Reference.GetKey(), resetItem);
		}
		_appliedItemsVersion = session.ItemsVersion;
		appliedChanges.Clear();
		appliedChanges.Add(reset);
	}

	private BrowseItemViewModelChange ProjectChange(BrowseItemChange change) =>
		change switch
		{
			BrowseItemAdded added => new BrowseItemViewModelAdded(added.Index, CreateItemViewModel(added.Item)),
			BrowseItemRemoved removed => new BrowseItemViewModelRemoved(removed.Index),
			BrowseItemReplaced replaced => new BrowseItemViewModelReplaced(replaced.Index, CreateItemViewModel(replaced.NewItem)),
			BrowseItemMoved moved => new BrowseItemViewModelMoved(moved.PreviousIndex, moved.CurrentIndex),
			BrowseItemsReset reset => new BrowseItemViewModelsReset(reset.Items.Select(CreateItemViewModel).ToArray()),
			_ => throw new InvalidOperationException($"Unsupported Core browse item change '{change.GetType().Name}'."),
		};

	private BrowseItemViewModel CreateItemViewModel(IStorableModel item)
	{
		var viewModel = new BrowseItemViewModel(item.Name, item is IFolderModel, item.Reference);
		viewModel.SetDetailsColumns(_detailsColumns);
		if (_pane.BrowseSession.TryGetPresentation(item.Reference.GetKey(), out var presentation))
		{
			if (presentation.Properties.Count is not 0)
			{
				viewModel.SetProperties(presentation.Properties);
			}

			if (presentation.Thumbnail is { } thumbnail)
			{
				QueuePendingThumbnail(item.Reference.GetKey(), thumbnail);
			}
		}

		return viewModel;
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

	private static string GetLocationText(BrowseLocation? location)
	{
		return location switch
		{
			HomeLocation => Strings.Home.GetLocalized(),
			FolderLocation folder when folder.Folder.LastKnownAddress is
				{ Scheme: var scheme, Value: var value }
				&& string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase)
				=> value,
			FolderLocation folder => folder.Folder.LastKnownAddress?.ToString()
				?? folder.Folder.ItemId,
			_ => location?.GetType().Name ?? Strings.Home.GetLocalized(),
		};
	}

	private void EnsureActive() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

	private sealed record PendingItemBatch(long PreviousVersion, long Version, IReadOnlyList<BrowseItemViewModelChange> Changes);

	private sealed record PendingColumns(long Generation, IReadOnlyList<DetailsColumnViewModel> Columns);

	private sealed record PendingState(bool IsLoading, string? ErrorMessage, string LocationText);
}
