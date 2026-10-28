// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Diagnostics;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Changes;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using System.Diagnostics;

namespace Files.Core.Browsing;

/// <summary>Coordinates location loading, item enumeration, selection, and presentation for one browse pane.</summary>
public sealed partial class BrowseSession : IBrowseSession, IBrowsePrefetchTarget, IInteractiveBrowseSession, IViewSettingsSession
{
	private const int InitialEnumerationBatchSize = 32;
	private const int SearchInitialEnumerationBatchSize = 1;
	private const int EnumerationBatchSize = 256;
	private const int MaximumEnumerationBatchSize = 1024;
	private static readonly TimeSpan PropertySortDebounce = TimeSpan.FromMilliseconds(150);

	private readonly IBrowseLocationResolver _locationResolver;
	private readonly IViewSettingsStore? _viewSettingsStore;
	private readonly IThumbnailCache? _thumbnailCache;
	private readonly Dictionary<BrowseLocation, BrowseViewSettings> _sessionViewSettings = [];
	private BrowseItemProjection _itemProjection;
	private readonly SemaphoreSlim _navigationLock = new(1, 1);
	private readonly BrowseChangeCoordinator _changeCoordinator;
	private readonly Lock _disposalLock = new();
	private readonly Lock _navigationCancellationLock = new();
	private readonly Lock _propertySortLock = new();
	private readonly BrowsePresentationStore _presentationStore = new();
	private readonly BrowseSelectionModel _selectionModel = new();
	private BrowseContextState? _activeContext;
	private BrowseContextState? _preparingContext;
	private Task? _disposeTask;
	private CancellationTokenSource? _activeNavigationCancellation;
	private CancellationTokenSource? _propertySortCancellation;
	private Task? _propertySortTask;
	private long _generationCounter;
	private long _itemsVersion;
	private long _diagnosticNavigationStartTimestamp;
	private BrowseViewCapabilities? _viewCapabilities;
	private bool _isDisposed;

	/// <inheritdoc />
	public BrowseLocation? Location { get; private set; }

	/// <inheritdoc />
	public IBrowseLocationContext? Context => Volatile.Read(ref _activeContext)?.Context;

	/// <inheritdoc />
	public long Generation => Volatile.Read(ref _activeContext)?.Generation ?? 0;

	/// <summary>Gets independent source loading states when browsing Home.</summary>
	public IReadOnlyList<HomeSourceState> HomeSources => (Context as HomeBrowseLocationContext)?.Sources ?? [];

	/// <inheritdoc />
	public IReadOnlyList<IStorableModel> Items => Volatile.Read(ref _itemProjection).Items;

	/// <inheritdoc />
	public int ItemCount => Volatile.Read(ref _itemProjection).Count;

	/// <inheritdoc />
	public bool Contains(StorableKey key)
	{
		return Volatile.Read(ref _itemProjection).Contains(key);
	}

	/// <inheritdoc />
	public bool TryGet(StorableKey key, out IStorableModel item)
	{
		return Volatile.Read(ref _itemProjection).TryGet(key, out item!);
	}

	/// <inheritdoc />
	public long ItemsVersion => Volatile.Read(ref _itemsVersion);

	/// <inheritdoc />
	public BrowseSelectionState Selection => _selectionModel.State;

	/// <inheritdoc />
	public BrowseViewSettings ViewSettings { get; private set; }

	/// <inheritdoc />
	public BrowseViewSettings PersistedViewSettings { get; private set; }

	/// <inheritdoc />
	public BrowseDisplaySettings DisplaySettings { get; private set; }

	/// <inheritdoc />
	public bool IsLoading { get; private set; }

	/// <inheritdoc />
	public Exception? Error { get; private set; }

	/// <inheritdoc />
	public event EventHandler? StateChanged;

	/// <inheritdoc />
	public event EventHandler<BrowseItemsChangedEventArgs>? ItemsChanged;

	/// <inheritdoc />
	public event EventHandler<BrowseItemPresentationChangedEventArgs>? ItemPresentationChanged;

	/// <inheritdoc />
	public event EventHandler? SelectionChanged;

	/// <summary>Initializes a browse session.</summary>
	/// <param name="locationResolver">The resolver used to open browse locations.</param>
	/// <param name="viewSettingsStore">The optional persistent view settings store.</param>
	/// <param name="thumbnailCache">The optional thumbnail cache.</param>
	public BrowseSession(IBrowseLocationResolver locationResolver, IViewSettingsStore? viewSettingsStore = null, IThumbnailCache? thumbnailCache = null)
	{
		ArgumentNullException.ThrowIfNull(locationResolver);

		_locationResolver = locationResolver;
		_viewSettingsStore = viewSettingsStore;
		_thumbnailCache = thumbnailCache;
		_changeCoordinator = new BrowseChangeCoordinator(ProcessPendingChangesAsync);
		_itemProjection = new BrowseItemProjection(BrowseViewSettings.Default, _presentationStore.GetSortPropertyValue);
		ViewSettings = BrowseViewSettings.Default;
		PersistedViewSettings = BrowseViewSettings.Default;
		DisplaySettings = BrowseDisplaySettings.Default;
	}

	/// <inheritdoc />
	public ValueTask NavigateAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		return NavigateAsync(location, 0, cancellationToken);
	}

	ValueTask IInteractiveBrowseSession.NavigateAsync(BrowseLocation location, nint ownerWindowHandle, CancellationToken cancellationToken)
	{
		return NavigateAsync(location, ownerWindowHandle, cancellationToken);
	}

	private async ValueTask NavigateAsync(BrowseLocation location, nint ownerWindowHandle, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);

		ArgumentNullException.ThrowIfNull(location);

		using var navigation = BeginNavigation(cancellationToken);
		var persistedSettingsTask = _viewSettingsStore?.GetAsync(location, navigation.Token).AsTask();
		if (persistedSettingsTask is not null)
		{
			await persistedSettingsTask.ConfigureAwait(false);
		}

		await _navigationLock.WaitAsync(navigation.Token).ConfigureAwait(false);

		try
		{
			await NavigateCoreAsync(location, ownerWindowHandle, persistedSettingsTask, navigation.Token).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	private async ValueTask NavigateCoreAsync(BrowseLocation location, nint ownerWindowHandle, Task<BrowseViewSettings?>? persistedSettingsTask, CancellationToken cancellationToken)
	{
		var propertySortTask = CancelPendingPropertySort();
		if (propertySortTask is not null)
		{
			await propertySortTask.ConfigureAwait(false);
		}

		var navigationStartTimestamp = Stopwatch.GetTimestamp();
		Volatile.Write(ref _diagnosticNavigationStartTimestamp, navigationStartTimestamp);
		CoreDiagnosticLog.Write("BrowseSession", $"Navigate START location={location.GetType().Name} thread={Environment.CurrentManagedThreadId}");
		IsLoading = true;
		Error = null;
		OnStateChanged();

		var nextItems = new List<IStorableModel>();
		IBrowseLocationContext? nextLocationContext = null;
		BrowseContextState? nextContext = null;
		BrowseNavigationRollbackState? previousState = null;
		var nextProjection = (BrowseItemProjection?)null;
		var enumerationActivated = false;
		var committed = false;

		try
		{
			nextLocationContext = await _locationResolver.OpenAsync(location, cancellationToken).ConfigureAwait(false);
			ArgumentNullException.ThrowIfNull(nextLocationContext);

			CoreDiagnosticLog.Write("BrowseSession", $"Folder resolved elapsedMs={Stopwatch.GetElapsedTime(navigationStartTimestamp).TotalMilliseconds:F1}");

			var changes = nextLocationContext.ChangeSource ?? nextLocationContext.LocationModel?.Get<IFolderChangeSource>();
			var generation = Interlocked.Increment(ref _generationCounter);
			nextContext = new BrowseContextState(this, nextLocationContext, changes, generation);
			Volatile.Write(ref _preparingContext, nextContext);

			var nextPersistedViewSettings = persistedSettingsTask is null
				? _sessionViewSettings.GetValueOrDefault(location, BrowseViewSettings.Default)
				: await persistedSettingsTask.ConfigureAwait(false) ?? BrowseViewSettings.Default;
			var nextViewSettings = nextLocationContext is IViewSettingsCapabilityProvider
				? BrowseViewSettingsResolver.DeferCapabilityDependentSettings(nextPersistedViewSettings)
				: nextPersistedViewSettings;

			await nextContext.StartAsync(cancellationToken).ConfigureAwait(false);
			nextProjection = new BrowseItemProjection(nextViewSettings, _presentationStore.GetSortPropertyValue);
			var targetBatchSize = location is SearchLocation or HomeLocation ? SearchInitialEnumerationBatchSize : InitialEnumerationBatchSize;
			var pendingBatch = new List<IStorableModel>(targetBatchSize);
			var firstItemReturned = false;
			CoreDiagnosticLog.Write("BrowseSession", $"Enumeration START generation={generation} elapsedMs={Stopwatch.GetElapsedTime(navigationStartTimestamp).TotalMilliseconds:F1}");
			var nextItemSequence = ownerWindowHandle is not 0 && nextLocationContext is IInteractiveBrowseLocationContext interactiveContext
				? interactiveContext.GetItemsAsync(ownerWindowHandle, cancellationToken)
				: nextLocationContext.GetItemsAsync(cancellationToken);
			await foreach (var item in nextItemSequence.ConfigureAwait(false))
			{
				if (ShouldHideItem(DisplaySettings, item))
				{
					await item.DisposeAsync().ConfigureAwait(false);

					continue;
				}

				nextItems.Add(item);
				pendingBatch.Add(item);
				if (!firstItemReturned)
				{
					firstItemReturned = true;
					var firstItemElapsedMilliseconds = Stopwatch.GetElapsedTime(navigationStartTimestamp).TotalMilliseconds;
					CoreDiagnosticLog.Write("BrowseSession", $"First storage item returned generation={generation} elapsedMs={firstItemElapsedMilliseconds:F1}");
					CoreDiagnosticLog.Write("BrowseSession", $"First item AppModel available generation={generation} elapsedMs={firstItemElapsedMilliseconds:F1}");
				}

				if (pendingBatch.Count < targetBatchSize)
				{
					continue;
				}

				var batchToPublish = enumerationActivated
					? pendingBatch
					: await SortInitialEnumerationBatchAsync(nextLocationContext, nextProjection, pendingBatch, nextViewSettings, cancellationToken).ConfigureAwait(false);
				PublishEnumerationBatch(location, nextPersistedViewSettings, nextViewSettings, nextContext, nextProjection, batchToPublish, ref previousState, ref enumerationActivated);
				pendingBatch.Clear();
				targetBatchSize = location is HomeLocation ? 1 : targetBatchSize switch
				{
					SearchInitialEnumerationBatchSize => InitialEnumerationBatchSize,
					InitialEnumerationBatchSize => EnumerationBatchSize,
					_ => Math.Min(MaximumEnumerationBatchSize, checked(targetBatchSize * 2)),
				};
				await Task.Yield();
			}
			CoreDiagnosticLog.Write("BrowseSession", $"Enumeration END generation={generation} items={nextItems.Count} elapsedMs={Stopwatch.GetElapsedTime(navigationStartTimestamp).TotalMilliseconds:F1}");

			if (pendingBatch.Count is not 0)
			{
				IReadOnlyList<IStorableModel> finalBatch = enumerationActivated
					? pendingBatch
					: await SortInitialEnumerationBatchAsync(nextLocationContext, nextProjection, pendingBatch, nextViewSettings, cancellationToken).ConfigureAwait(false);
				PublishEnumerationBatch(location, nextPersistedViewSettings, nextViewSettings, nextContext, nextProjection, finalBatch, ref previousState, ref enumerationActivated);
			}
			else if (!enumerationActivated)
			{
				PublishEnumerationBatch(location, nextPersistedViewSettings, nextViewSettings, nextContext, nextProjection, [], ref previousState, ref enumerationActivated);
			}

			var sortStartTimestamp = Stopwatch.GetTimestamp();
			var finalSortChanges = await SortProjectionAsync(nextLocationContext, nextProjection, nextViewSettings, cancellationToken).ConfigureAwait(false);
			PublishItemsChanged(finalSortChanges);
			CoreDiagnosticLog.Write(
				"BrowseSession",
				$"Initial sort completed generation={generation} changed={!finalSortChanges.IsEmpty} " +
				$"elapsedMs={Stopwatch.GetElapsedTime(sortStartTimestamp).TotalMilliseconds:F1}");

			var nextSelection = Equals(previousState!.Location, location)
				? BrowseSelectionModel.Normalize(previousState.Selection, nextProjection)
				: BrowseSelectionState.Empty;
			Volatile.Write(ref _preparingContext, null);
			Error = null;
			SetSelectionState(nextSelection);
			_changeCoordinator.Signal();
			committed = true;

			var previousContext = previousState.ActiveContext;
			var previousItems = previousState.Items;
			nextLocationContext = null;
			nextContext = null;
			try
			{
				await DisposeItemsAsync(previousItems).ConfigureAwait(false);
			}
			finally
			{
				if (previousContext is not null)
				{
					await previousContext.DisposeAsync().ConfigureAwait(false);
				}
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			Error = exception;
			throw;
		}
		finally
		{
			if (!committed)
			{
				if (enumerationActivated && previousState is not null)
				{
					RestoreNavigationState(previousState);
				}
				if (nextContext is not null)
				{
					Volatile.Write(ref _preparingContext, null);
					_changeCoordinator.TryClearFullRefresh(nextContext.Generation);
				}

				try
				{
					await DisposeItemsAsync(nextItems).ConfigureAwait(false);
				}
				finally
				{
					if (nextContext is not null)
					{
						await nextContext.DisposeAsync().ConfigureAwait(false);
					}
					else if (nextLocationContext is not null)
					{
						await nextLocationContext.DisposeAsync().ConfigureAwait(false);
					}
				}
			}

			IsLoading = false;
			OnStateChanged();
			CoreDiagnosticLog.Write(
				"BrowseSession",
				$"Navigate END location={location.GetType().Name} items={ItemCount} loading={IsLoading} error={Error is not null} " +
				$"elapsedMs={Stopwatch.GetElapsedTime(navigationStartTimestamp).TotalMilliseconds:F1}");
		}
	}

	private void PublishEnumerationBatch(
		BrowseLocation location,
		BrowseViewSettings persistedSettings,
		BrowseViewSettings settings,
		BrowseContextState context,
		BrowseItemProjection projection,
		IReadOnlyList<IStorableModel> batch,
		ref BrowseNavigationRollbackState? previousState,
		ref bool activated)
	{
		var changes = projection.AddRange(batch, preserveInputOrder: true);
		var navigationStartTimestamp = Volatile.Read(ref _diagnosticNavigationStartTimestamp);
		CoreDiagnosticLog.Write(
			"BrowseSession",
			$"PublishEnumerationBatch activated={activated} batchItems={batch.Count} changes={changes.Changes.Count} projectedItems={projection.Count} " +
			$"elapsedMs={Stopwatch.GetElapsedTime(navigationStartTimestamp).TotalMilliseconds:F1}");
		if (!activated)
		{
			previousState ??= CaptureNavigationState();
			var provisionalSelection = Equals(previousState.Location, location)
				? BrowseSelectionModel.Normalize(previousState.Selection, projection)
				: BrowseSelectionState.Empty;
			var currentProjection = Volatile.Read(ref _itemProjection);
			currentProjection.InvokeLocked(() =>
			{
				Location = location;
				Volatile.Write(ref _activeContext, context);
				PersistedViewSettings = persistedSettings;
				ViewSettings = settings;
				_viewCapabilities = null;
				Error = null;
				_presentationStore.Clear();
				Volatile.Write(ref _itemProjection, projection);
			});
			SetSelectionState(provisionalSelection);
			PublishItemsChanged(new BrowseItemChangeSet([new BrowseItemsReset(projection.Items)]));
			OnStateChanged();
			activated = true;

			return;
		}

		if (Equals(previousState!.Location, location))
		{
			SetSelectionState(BrowseSelectionModel.Normalize(previousState.Selection, projection));
		}

		PublishItemsChanged(changes);
	}

	private BrowseNavigationRollbackState CaptureNavigationState()
	{
		return new BrowseNavigationRollbackState(Location, Volatile.Read(ref _activeContext), Volatile.Read(ref _itemProjection), PersistedViewSettings, ViewSettings, _viewCapabilities, Selection, Items,
			_presentationStore.CaptureState());
	}

	private void RestoreNavigationState(BrowseNavigationRollbackState previousState)
	{
		ArgumentNullException.ThrowIfNull(previousState);

		var currentProjection = Volatile.Read(ref _itemProjection);
		currentProjection.InvokeLocked(() =>
		{
			Location = previousState.Location;
			Volatile.Write(ref _activeContext, previousState.ActiveContext);
			Volatile.Write(ref _preparingContext, null);
			Volatile.Write(ref _itemProjection, previousState.ItemProjection);
			PersistedViewSettings = previousState.PersistedViewSettings;
			ViewSettings = previousState.ViewSettings;
			_viewCapabilities = previousState.ViewCapabilities;
			_presentationStore.RestoreState(previousState.Presentations);
		});
		SetSelectionState(previousState.Selection);
		PublishItemsChanged(new BrowseItemChangeSet([new BrowseItemsReset(previousState.Items)]));
		OnStateChanged();
	}

	/// <inheritdoc />
	public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);

		return Location is null
			? ValueTask.CompletedTask
			: NavigateAsync(Location, cancellationToken);
	}

	ValueTask IInteractiveBrowseSession.RefreshAsync(nint ownerWindowHandle, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);

		return Location is null
			? ValueTask.CompletedTask
			: NavigateAsync(Location, ownerWindowHandle, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask UpdateViewSettingsAsync(BrowseViewSettings settings, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);

		ArgumentNullException.ThrowIfNull(settings);

		var propertySortTask = CancelPendingPropertySort();
		if (propertySortTask is not null)
		{
			await propertySortTask.ConfigureAwait(false);
		}

		BrowseItemPresentationChangedEventArgs[] clearedThumbnails = [];
		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (Location is null)
			{
				throw new InvalidOperationException("View settings require an active browse location.");
			}

			if (_viewSettingsStore is not null)
			{
				await _viewSettingsStore.SetAsync(Location, settings, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				_sessionViewSettings[Location] = settings;
			}

			PersistedViewSettings = settings;
			var effectiveSettings = _viewCapabilities is not null
				? BrowseViewSettingsResolver.Resolve(settings, _viewCapabilities)
				: Volatile.Read(ref _activeContext)?.Context is IViewSettingsCapabilityProvider
					? BrowseViewSettingsResolver.DeferCapabilityDependentSettings(settings)
					: settings;
			clearedThumbnails = await ApplyEffectiveViewSettingsAsync(effectiveSettings, cancellationToken, forceExternalSort: true).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
		}

		foreach (var thumbnail in clearedThumbnails)
		{
			RaiseEvent(ItemPresentationChanged, thumbnail);
		}
	}

	/// <inheritdoc />
	public async ValueTask ApplyViewCapabilitiesAsync(BrowseViewCapabilities capabilities, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);

		ArgumentNullException.ThrowIfNull(capabilities);

		var propertySortTask = CancelPendingPropertySort();
		if (propertySortTask is not null)
		{
			await propertySortTask.ConfigureAwait(false);
		}

		BrowseItemPresentationChangedEventArgs[] clearedThumbnails = [];
		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (Location is null || Volatile.Read(ref _activeContext) is null)
			{

				return;
			}

			_viewCapabilities = capabilities;
			var effectiveSettings = BrowseViewSettingsResolver.Resolve(PersistedViewSettings, capabilities);
			if (Equals(ViewSettings, effectiveSettings))
			{

				return;
			}

			clearedThumbnails = await ApplyEffectiveViewSettingsAsync(effectiveSettings, cancellationToken, forceExternalSort: false).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
		}

		foreach (var thumbnail in clearedThumbnails)
		{
			RaiseEvent(ItemPresentationChanged, thumbnail);
		}
	}

	private async ValueTask<BrowseItemPresentationChangedEventArgs[]> ApplyEffectiveViewSettingsAsync(BrowseViewSettings settings, CancellationToken cancellationToken, bool forceExternalSort)
	{
		var previousLayoutMode = ViewSettings.LayoutMode;
		var context = Volatile.Read(ref _activeContext)?.Context ?? throw new InvalidOperationException("View settings require an active browse location.");
		var projection = Volatile.Read(ref _itemProjection);
		var sortChanged = ViewSettings.SortPropertyId != settings.SortPropertyId || ViewSettings.SortDirection != settings.SortDirection;
		var changes = projection.UpdateSort(settings, deferSort: context is IBrowseLocationItemSorter);
		ViewSettings = settings;
		if (context is IBrowseLocationItemSorter && (forceExternalSort || sortChanged))
		{
			changes = await SortProjectionAsync(context, projection, settings, cancellationToken).ConfigureAwait(false);
		}

		BrowseItemPresentationChangedEventArgs[] clearedThumbnails = previousLayoutMode != settings.LayoutMode ? _presentationStore.ClearThumbnails() : [];
		PublishItemsChanged(changes);
		OnStateChanged();

		return clearedThumbnails;
	}

	/// <inheritdoc />
	public async ValueTask UpdateDisplaySettingsAsync(BrowseDisplaySettings settings, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);

		ArgumentNullException.ThrowIfNull(settings);

		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (DisplaySettings == settings)
			{
				return;
			}

			DisplaySettings = settings;
			OnStateChanged();
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	/// <inheritdoc />
	public bool TryGetPresentation(StorableKey key, out BrowseItemPresentation presentation)
	{
		return _presentationStore.TryGet(key, out presentation);
	}

	ValueTask<bool> IBrowsePrefetchTarget.PublishPropertiesAsync(
		long generation,
		IStorableModel item,
		IReadOnlyDictionary<string, object?> properties,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(item);

		ArgumentNullException.ThrowIfNull(properties);

		cancellationToken.ThrowIfCancellationRequested();
		var key = item.Reference.GetKey();
		var projection = Volatile.Read(ref _itemProjection);
		BrowseItemPresentationChangedEventArgs? presentationChanged = null;
		var applied = projection.TryApplyToCurrent(key, item, () =>
		{
			if (Generation != generation || !ReferenceEquals(projection, Volatile.Read(ref _itemProjection)))
			{
				return false;
			}

			var presentation = _presentationStore.UpdateProperties(generation, key, item, properties);
			presentationChanged = new BrowseItemPresentationChangedEventArgs(key, presentation, BrowseItemPresentationChangeFlags.Properties);

			if (!string.IsNullOrWhiteSpace(ViewSettings.SortPropertyId) && properties.ContainsKey(ViewSettings.SortPropertyId))
			{
				SchedulePropertySort(generation);
			}

			return true;
		});
		if (!applied)
		{
			return ValueTask.FromResult(false);
		}

		RaiseEvent(ItemPresentationChanged, presentationChanged!);

		return ValueTask.FromResult(true);
	}

	ValueTask<bool> IBrowsePrefetchTarget.PublishThumbnailAsync(long generation, IStorableModel item, ThumbnailResult thumbnail, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(item);

		ArgumentNullException.ThrowIfNull(thumbnail);

		cancellationToken.ThrowIfCancellationRequested();
		var key = item.Reference.GetKey();
		var projection = Volatile.Read(ref _itemProjection);
		BrowseItemPresentationChangedEventArgs? presentationChanged = null;
		var changed = false;
		var applied = projection.TryApplyToCurrent(key, item, () =>
		{
			if (Generation != generation || !ReferenceEquals(projection, Volatile.Read(ref _itemProjection)))
			{
				return false;
			}

			if (!_presentationStore.TryUpdateThumbnail(generation, key, item, thumbnail, out var presentation))
			{
				return true;
			}

			changed = true;
			presentationChanged = new BrowseItemPresentationChangedEventArgs(key, presentation, BrowseItemPresentationChangeFlags.Thumbnail);

			return true;
		});
		if (!applied)
		{
			return ValueTask.FromResult(false);
		}

		if (changed)
		{
			RaiseEvent(ItemPresentationChanged, presentationChanged!);
		}

		return ValueTask.FromResult(true);
	}

	/// <inheritdoc />
	public void SetSelection(IEnumerable<StorableKey> selectedKeys, StorableKey? focusedKey, StorableKey? anchorKey)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);

		ArgumentNullException.ThrowIfNull(selectedKeys);

		var requestedSelection = new BrowseSelectionState(Array.AsReadOnly(selectedKeys.ToArray()), focusedKey, anchorKey);
		while (true)
		{
			var version = ItemsVersion;
			var normalized = BrowseSelectionModel.Normalize(requestedSelection, Volatile.Read(ref _itemProjection));
			if (version != ItemsVersion)
			{
				continue;
			}

			if (!_selectionModel.Set(normalized))
			{
				return;
			}

			RaiseEvent(SelectionChanged);

			return;
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			Volatile.Write(ref _isDisposed, true);
			_disposeTask ??= DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		await _changeCoordinator.DisposeAsync().ConfigureAwait(false);
		var propertySortTask = CancelPendingPropertySort();
		if (propertySortTask is not null)
		{
			await propertySortTask.ConfigureAwait(false);
		}

		try
		{
			await _navigationLock.WaitAsync().ConfigureAwait(false);

			try
			{
				var items = Items;
				var currentContext = Volatile.Read(ref _activeContext);
				Volatile.Write(ref _itemProjection, new BrowseItemProjection(ViewSettings, _presentationStore.GetSortPropertyValue));
				_selectionModel.Set(BrowseSelectionState.Empty);
				_presentationStore.Clear();
				Volatile.Write(ref _activeContext, null);
				Volatile.Write(ref _preparingContext, null);
				PersistedViewSettings = BrowseViewSettings.Default;
				_viewCapabilities = null;
				_sessionViewSettings.Clear();

				try
				{
					await DisposeItemsAsync(items).ConfigureAwait(false);
				}
				finally
				{
					if (currentContext is not null)
					{
						await currentContext.DisposeAsync().ConfigureAwait(false);
					}
				}
			}
			finally
			{
				_navigationLock.Release();
			}
		}
		finally
		{
			_navigationLock.Dispose();
			GC.SuppressFinalize(this);
		}
	}

	private void SchedulePropertySort(long generation)
	{
		CancellationTokenSource? previousCancellation;
		CancellationTokenSource cancellation;
		lock (_propertySortLock)
		{
			previousCancellation = _propertySortCancellation;
			cancellation = new CancellationTokenSource();
			_propertySortCancellation = cancellation;
			_propertySortTask = ApplyPropertySortAsync(generation, cancellation);
		}

		try
		{
			if (previousCancellation is not null)
			{
				CoreDiagnosticLog.Write("BrowseSession", "Previous navigation cancelled");
			}

			previousCancellation?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private async Task ApplyPropertySortAsync(long generation, CancellationTokenSource cancellation)
	{
		try
		{
			await Task.Delay(PropertySortDebounce, cancellation.Token).ConfigureAwait(false);
			await _navigationLock.WaitAsync(cancellation.Token).ConfigureAwait(false);
			try
			{
				if (Volatile.Read(ref _isDisposed) || Generation != generation)
				{
					return;
				}

				var context = Volatile.Read(ref _activeContext);
				if (context is null)
				{
					return;
				}

				var projection = Volatile.Read(ref _itemProjection);
				var changes = await SortProjectionAsync(context.Context, projection, ViewSettings, cancellation.Token).ConfigureAwait(false);
				PublishItemsChanged(changes);
			}
			finally
			{
				_navigationLock.Release();
			}
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		finally
		{
			lock (_propertySortLock)
			{
				if (ReferenceEquals(_propertySortCancellation, cancellation))
				{
					_propertySortCancellation = null;
					_propertySortTask = null;
				}
			}

			cancellation.Dispose();
		}
	}

	private Task? CancelPendingPropertySort()
	{
		CancellationTokenSource? cancellation;
		Task? task;
		lock (_propertySortLock)
		{
			cancellation = _propertySortCancellation;
			task = _propertySortTask;
		}

		try
		{
			cancellation?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}

		return task;
	}

	private static async ValueTask<BrowseItemChangeSet> SortProjectionAsync(
		IBrowseLocationContext context,
		BrowseItemProjection projection,
		BrowseViewSettings settings,
		CancellationToken cancellationToken)
	{
		if (context is IBrowseLocationItemSorter sorter)
		{
			var sortedItems = await sorter.SortItemsAsync(projection.Items, settings, cancellationToken).ConfigureAwait(false);
			if (sortedItems is not null)
			{
				return projection.ApplyExternalOrder(sortedItems);
			}
		}

		return projection.RefreshSort();
	}

	private static async ValueTask<IReadOnlyList<IStorableModel>> SortInitialEnumerationBatchAsync(
		IBrowseLocationContext context,
		BrowseItemProjection projection,
		IReadOnlyList<IStorableModel> items,
		BrowseViewSettings settings,
		CancellationToken cancellationToken)
	{
		if (context is IBrowseLocationItemSorter sorter)
		{
			var sortedItems = await sorter.SortItemsAsync(items, settings, cancellationToken).ConfigureAwait(false);
			if (sortedItems is not null)
			{
				return sortedItems;
			}
		}

		return projection.SortItems(items);
	}

	private void PublishItemsChanged(BrowseItemChangeSet changeSet)
	{
		if (changeSet.IsEmpty)
		{
			return;
		}

		var previousVersion = Interlocked.Read(ref _itemsVersion);
		var version = Interlocked.Increment(ref _itemsVersion);
		var eventStartTimestamp = Stopwatch.GetTimestamp();
		CoreDiagnosticLog.Write("BrowseSession", $"ItemsChanged START version={version} previous={previousVersion} changes={changeSet.Changes.Count} items={Items.Count}");
		RaiseEvent(ItemsChanged, new BrowseItemsChangedEventArgs(previousVersion, version, changeSet.Changes));
		CoreDiagnosticLog.Write("BrowseSession", $"ItemsChanged END version={version} callbackMs={Stopwatch.GetElapsedTime(eventStartTimestamp).TotalMilliseconds:F1}");
	}

	private void SetSelectionState(BrowseSelectionState nextSelection)
	{
		ArgumentNullException.ThrowIfNull(nextSelection);

		if (!_selectionModel.Set(nextSelection))
		{
			return;
		}

		RaiseEvent(SelectionChanged);
	}

	private void RemoveSelectionKey(StorableKey key)
	{
		if (_selectionModel.Remove(key))
		{
			RaiseEvent(SelectionChanged);
		}
	}

	private void MigrateSelection(StorableKey previousKey, StorableKey currentKey)
	{
		if (_selectionModel.Migrate(previousKey, currentKey))
		{
			RaiseEvent(SelectionChanged);
		}
	}

	private sealed record BrowseNavigationRollbackState(
		BrowseLocation? Location,
		BrowseContextState? ActiveContext,
		BrowseItemProjection ItemProjection,
		BrowseViewSettings PersistedViewSettings,
		BrowseViewSettings ViewSettings,
		BrowseViewCapabilities? ViewCapabilities,
		BrowseSelectionState Selection,
		IReadOnlyList<IStorableModel> Items,
		BrowsePresentationStore.State Presentations);

	private sealed class NavigationOperation : IDisposable
	{
		private readonly BrowseSession _owner;
		private readonly CancellationTokenSource _cancellation;
		private int _isDisposed;

		public CancellationToken Token => _cancellation.Token;

		public NavigationOperation(BrowseSession owner, CancellationTokenSource cancellation)
		{
			_owner = owner;
			_cancellation = cancellation;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
			{
				return;
			}

			_owner.EndNavigation(_cancellation);
		}
	}

	private enum IncrementalApplyResult
	{
		Applied,
		Stale,
		RequiresFullRefresh,
	}

	private enum ItemLookupResult
	{
		Missing,
		Found,
		Ambiguous,
	}

	private sealed class BrowseContextState : IAsyncDisposable
	{
		private readonly BrowseSession _owner;

		private readonly IFolderChangeSource? _changes;

		private int _handlersAttached;

		public IBrowseLocationContext Context { get; }

		public long Generation { get; }

		public BrowseContextState(BrowseSession owner, IBrowseLocationContext context, IFolderChangeSource? changes, long generation)
		{
			_owner = owner;
			Context = context;
			_changes = changes;
			Generation = generation;
		}

		public async ValueTask StartAsync(CancellationToken cancellationToken)
		{
			if (_changes is null)
			{
				return;
			}

			_changes.Changed += OnChanged;
			_changes.Faulted += OnFaulted;
			if (Context is HomeBrowseLocationContext home)
			{
				home.SourceStatesChanged += OnSourceStatesChanged;
			}
			Volatile.Write(ref _handlersAttached, 1);

			try
			{
				await _changes.StartAsync(cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				Detach();
				throw;
			}
		}

		public async ValueTask DisposeAsync()
		{
			Detach();
			await Context.DisposeAsync().ConfigureAwait(false);
		}

		private void Detach()
		{
			if (_changes is null || Interlocked.Exchange(ref _handlersAttached, 0) is 0)
			{
				return;
			}

			_changes.Changed -= OnChanged;
			_changes.Faulted -= OnFaulted;
			if (Context is HomeBrowseLocationContext home)
			{
				home.SourceStatesChanged -= OnSourceStatesChanged;
			}
		}

		private void OnSourceStatesChanged(object? sender, EventArgs args)
		{
			_owner.OnStateChanged();
		}

		private void OnChanged(object? sender, FolderChangeEventArgs args)
		{
			_owner.OnFolderChanged(this, args.Change);
		}

		private void OnFaulted(object? sender, FolderChangeErrorEventArgs args)
		{
			_owner.OnFolderChangeFaulted(this, args);
		}
	}

	private void OnStateChanged()
	{
		var eventStartTimestamp = Stopwatch.GetTimestamp();
		RaiseEvent(StateChanged);
		CoreDiagnosticLog.Write("BrowseSession", $"StateChanged loading={IsLoading} items={Items.Count} callbackMs={Stopwatch.GetElapsedTime(eventStartTimestamp).TotalMilliseconds:F1}");
	}

	private NavigationOperation BeginNavigation(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _changeCoordinator.LifetimeToken);
		CancellationTokenSource? previousCancellation;
		lock (_navigationCancellationLock)
		{
			previousCancellation = _activeNavigationCancellation;
			_activeNavigationCancellation = operationCancellation;
		}

		try
		{
			previousCancellation?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}

		return new NavigationOperation(this, operationCancellation);
	}

	private void EndNavigation(CancellationTokenSource operationCancellation)
	{
		lock (_navigationCancellationLock)
		{
			if (ReferenceEquals(_activeNavigationCancellation, operationCancellation))
			{
				_activeNavigationCancellation = null;
			}
		}

		operationCancellation.Dispose();
	}

	private void RaiseEvent(EventHandler? handlers)
	{
		if (handlers is null)
		{
			return;
		}

		foreach (EventHandler handler in handlers.GetInvocationList())
		{
			try
			{
				handler(this, EventArgs.Empty);
			}
			catch (Exception exception)
			{
				Trace.TraceError("BrowseSession event handler failed: {0}", exception);
			}
		}
	}

	private void RaiseEvent<TEventArgs>(EventHandler<TEventArgs>? handlers, TEventArgs args)
		where TEventArgs : EventArgs
	{
		if (handlers is null)
		{
			return;
		}

		foreach (EventHandler<TEventArgs> handler in handlers.GetInvocationList())
		{
			try
			{
				handler(this, args);
			}
			catch (Exception exception)
			{
				Trace.TraceError("BrowseSession event handler failed: {0}", exception);
			}
		}
	}

	private static async ValueTask DisposeItemsAsync(IEnumerable<IStorableModel> items)
	{
		List<Exception>? errors = null;
		foreach (var item in items)
		{
			try
			{
				await item.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception error)
			{
				(errors ??= []).Add(error);
			}
		}

		if (errors is { Count: 1 })
		{
			throw errors[0];
		}

		if (errors is { Count: > 1 })
		{
			throw new AggregateException("One or more browse items could not be disposed.", errors);
		}
	}
}
