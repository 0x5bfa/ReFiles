// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Diagnostics;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Changes;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using System.Diagnostics;

namespace Files.Core.Browsing;

public sealed class BrowseSession : IBrowseSession, IBrowsePrefetchTarget
{
	private const int InitialEnumerationBatchSize = 32;
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
	private long _contentVersion;
	private long _itemsVersion;
	private long _diagnosticNavigationStartTimestamp;
	private bool _isDisposed;

	/// <inheritdoc />
	public BrowseLocation? Location { get; private set; }

	/// <inheritdoc />
	public IBrowseLocationContext? Context => Volatile.Read(ref _activeContext)?.Context;

	/// <inheritdoc />
	public long Generation => Volatile.Read(ref _activeContext)?.Generation ?? 0;

	/// <inheritdoc />
	public IReadOnlyList<IStorableModel> Items => Volatile.Read(ref _itemProjection).Items;

	/// <inheritdoc />
	public bool Contains(StorableKey key) => Volatile.Read(ref _itemProjection).Contains(key);

	/// <inheritdoc />
	public long ItemsVersion => Volatile.Read(ref _itemsVersion);

	long IBrowsePrefetchTarget.ContentVersion => Volatile.Read(ref _contentVersion);

	/// <inheritdoc />
	public BrowseSelectionState Selection => _selectionModel.State;

	/// <inheritdoc />
	public BrowseViewSettings ViewSettings { get; private set; }

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
	}

	/// <inheritdoc />
	public async ValueTask NavigateAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);
		ArgumentNullException.ThrowIfNull(location);

		using var navigation = BeginNavigation(cancellationToken);
		await _navigationLock.WaitAsync(navigation.Token).ConfigureAwait(false);

		try
		{
			await NavigateCoreAsync(location, navigation.Token).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	private async ValueTask NavigateCoreAsync(BrowseLocation location, CancellationToken cancellationToken)
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
		BrowseNavigationSnapshot? previousState = null;
		var nextProjection = (BrowseItemProjection?)null;
		var enumerationActivated = false;
		var committed = false;

		try
		{
			nextLocationContext = await _locationResolver.OpenAsync(location, cancellationToken).ConfigureAwait(false);
			ArgumentNullException.ThrowIfNull(nextLocationContext);
			CoreDiagnosticLog.Write("BrowseSession", $"Folder resolved elapsedMs={Stopwatch.GetElapsedTime(navigationStartTimestamp).TotalMilliseconds:F1}");

			var changes = nextLocationContext.LocationModel?.Get<IFolderChangeSource>();
			var generation = Interlocked.Increment(ref _generationCounter);
			nextContext = new BrowseContextState(this, nextLocationContext, changes, generation);
			Volatile.Write(ref _preparingContext, nextContext);

			var nextViewSettings = _viewSettingsStore is null
				? _sessionViewSettings.GetValueOrDefault(location, BrowseViewSettings.Default)
				: await _viewSettingsStore.GetAsync(location, cancellationToken).ConfigureAwait(false)
					?? BrowseViewSettings.Default;

			await nextContext.StartAsync(cancellationToken).ConfigureAwait(false);
			nextProjection = new BrowseItemProjection(nextViewSettings, _presentationStore.GetSortPropertyValue);
			var pendingBatch = new List<IStorableModel>(InitialEnumerationBatchSize);
			var targetBatchSize = InitialEnumerationBatchSize;
			var firstItemReturned = false;
			CoreDiagnosticLog.Write("BrowseSession", $"Enumeration START generation={generation} elapsedMs={Stopwatch.GetElapsedTime(navigationStartTimestamp).TotalMilliseconds:F1}");
			await foreach (var item in nextLocationContext.GetItemsAsync(cancellationToken).ConfigureAwait(false))
			{
				if (ShouldHideItem(nextViewSettings, item))
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

				PublishEnumerationBatch(location, nextViewSettings, nextContext, nextProjection, pendingBatch, ref previousState, ref enumerationActivated);
				pendingBatch.Clear();
				targetBatchSize = Math.Min(MaximumEnumerationBatchSize, enumerationActivated && targetBatchSize is InitialEnumerationBatchSize ? EnumerationBatchSize : checked(targetBatchSize * 2));
				await Task.Yield();
			}
			CoreDiagnosticLog.Write("BrowseSession", $"Enumeration END generation={generation} items={nextItems.Count} elapsedMs={Stopwatch.GetElapsedTime(navigationStartTimestamp).TotalMilliseconds:F1}");

			if (pendingBatch.Count is not 0)
			{
				IReadOnlyList<IStorableModel> finalBatch = enumerationActivated ? pendingBatch : nextProjection.SortItems(pendingBatch);
				PublishEnumerationBatch(location, nextViewSettings, nextContext, nextProjection, finalBatch, ref previousState, ref enumerationActivated);
			}
			else if (!enumerationActivated)
			{
				PublishEnumerationBatch(location, nextViewSettings, nextContext, nextProjection, [], ref previousState, ref enumerationActivated);
			}

			var sortStartTimestamp = Stopwatch.GetTimestamp();
			var finalSortChanges = nextProjection.Sort();
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
				$"Navigate END location={location.GetType().Name} items={Items.Count} loading={IsLoading} error={Error is not null} " +
				$"elapsedMs={Stopwatch.GetElapsedTime(navigationStartTimestamp).TotalMilliseconds:F1}");
		}
	}

	private void PublishEnumerationBatch(
		BrowseLocation location,
		BrowseViewSettings settings,
		BrowseContextState context,
		BrowseItemProjection projection,
		IReadOnlyList<IStorableModel> batch,
		ref BrowseNavigationSnapshot? previousState,
		ref bool activated)
	{
		var changes = projection.AddRange(batch, preserveInputOrder: true);
		var navigationStartTimestamp = Volatile.Read(ref _diagnosticNavigationStartTimestamp);
		CoreDiagnosticLog.Write(
			"BrowseSession",
			$"PublishEnumerationBatch activated={activated} batchItems={batch.Count} changes={changes.Changes.Count} projectedItems={projection.Items.Count} " +
			$"elapsedMs={Stopwatch.GetElapsedTime(navigationStartTimestamp).TotalMilliseconds:F1}");
		if (!activated)
		{
			previousState ??= CaptureNavigationState();
			var provisionalSelection = Equals(previousState.Location, location)
				? BrowseSelectionModel.Normalize(previousState.Selection, projection)
				: BrowseSelectionState.Empty;
			Location = location;
			Volatile.Write(ref _activeContext, context);
			ViewSettings = settings;
			Error = null;
			_presentationStore.Clear();
			SetSelectionState(provisionalSelection);
			Volatile.Write(ref _itemProjection, projection);
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

	private BrowseNavigationSnapshot CaptureNavigationState()
	{
		return new BrowseNavigationSnapshot(Location, Volatile.Read(ref _activeContext), Volatile.Read(ref _itemProjection), ViewSettings, Selection, Items, _presentationStore.Capture());
	}

	private void RestoreNavigationState(BrowseNavigationSnapshot previousState)
	{
		ArgumentNullException.ThrowIfNull(previousState);

		Location = previousState.Location;
		Volatile.Write(ref _activeContext, previousState.ActiveContext);
		Volatile.Write(ref _preparingContext, null);
		Volatile.Write(ref _itemProjection, previousState.ItemProjection);
		ViewSettings = previousState.ViewSettings;
		_presentationStore.Restore(previousState.Presentations);
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

	private async ValueTask ProcessPendingChangesAsync(CancellationToken cancellationToken)
	{
		var currentContext = Volatile.Read(ref _activeContext);
		if (currentContext is null)
		{
			return;
		}

		try
		{
			if (await ProcessRequestedFullRefreshAsync(currentContext, cancellationToken).ConfigureAwait(false))
			{
				return;
			}

			await ProcessChangesAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			// NavigateCoreAsync records the refresh failure in Error.
		}
	}

	private async ValueTask<bool> ProcessRequestedFullRefreshAsync(BrowseContextState currentContext, CancellationToken cancellationToken)
	{
		var generation = _changeCoordinator.RequestedFullRefreshGeneration;
		if (generation is 0)
		{
			return false;
		}

		if (generation > currentContext.Generation)
		{
			return true;
		}

		if (generation < currentContext.Generation)
		{
			_changeCoordinator.TryClearFullRefresh(generation);

			return false;
		}

		if (!_changeCoordinator.TryClearFullRefresh(generation))
		{
			return false;
		}

		await RefreshCurrentAsync(generation, cancellationToken).ConfigureAwait(false);

		return true;
	}

	private async ValueTask ProcessChangesAsync(CancellationToken cancellationToken)
	{
		while (_changeCoordinator.TryRead(out var pendingChange))
		{
			var currentContext = Volatile.Read(ref _activeContext);
			if (currentContext is null)
			{
				_changeCoordinator.Defer(pendingChange);

				return;
			}

			if (pendingChange.Generation < currentContext.Generation)
			{
				continue;
			}

			if (pendingChange.Generation > currentContext.Generation)
			{
				if (Volatile.Read(ref _preparingContext)?.Generation == pendingChange.Generation)
				{
					_changeCoordinator.Defer(pendingChange);

					return;
				}

				continue;
			}

			if (_changeCoordinator.RequestedFullRefreshGeneration == currentContext.Generation)
			{
				return;
			}

			var result = await ApplyChangeAsync(pendingChange, cancellationToken).ConfigureAwait(false);
			if (result is IncrementalApplyResult.RequiresFullRefresh)
			{
				RequestFullRefresh(currentContext.Generation);

				return;
			}
		}
	}

	private async ValueTask RefreshCurrentAsync(long generation, CancellationToken cancellationToken)
	{
		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			var currentContext = Volatile.Read(ref _activeContext);
			if (currentContext is null || currentContext.Generation != generation)
			{
				return;
			}

			await NavigateCoreAsync(currentContext.Context.Location, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	private async ValueTask<IncrementalApplyResult> ApplyChangeAsync(BrowseQueuedChange pendingChange, CancellationToken cancellationToken)
	{
		var currentContext = Volatile.Read(ref _activeContext);
		if (currentContext is null || currentContext.Generation != pendingChange.Generation)
		{
			return IncrementalApplyResult.Stale;
		}

		try
		{
			if (pendingChange.Change.RequiresRefresh || pendingChange.Change.Kind is FolderChangeKind.DirectoryUpdated || currentContext.Context is not IBrowseLocationItemResolver resolver)
			{
				return IncrementalApplyResult.RequiresFullRefresh;
			}

			return pendingChange.Change.Kind switch
			{
				FolderChangeKind.Created => await ApplyCreatedAsync(currentContext, resolver, pendingChange.Change, cancellationToken).ConfigureAwait(false),
				FolderChangeKind.Deleted => await ApplyDeletedAsync(currentContext, pendingChange.Change, cancellationToken).ConfigureAwait(false),
				FolderChangeKind.Renamed => await ApplyRenamedAsync(currentContext, resolver, pendingChange.Change, cancellationToken).ConfigureAwait(false),
				FolderChangeKind.Updated => await ApplyUpdatedAsync(currentContext, resolver, pendingChange.Change, cancellationToken).ConfigureAwait(false),
				_ => IncrementalApplyResult.RequiresFullRefresh,
			};
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return IncrementalApplyResult.RequiresFullRefresh;
		}
	}

	private async ValueTask<IncrementalApplyResult> ApplyCreatedAsync(BrowseContextState context, IBrowseLocationItemResolver resolver, FolderChange change, CancellationToken cancellationToken)
	{
		if (!TryGetKey(change.CurrentItem, out var key))
		{
			return IncrementalApplyResult.RequiresFullRefresh;
		}

		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		ItemLookupResult lookup;
		try
		{
			if (!IsActiveGeneration(context))
			{
				return IncrementalApplyResult.Stale;
			}

			lookup = FindItemIndex(Volatile.Read(ref _itemProjection), key, out _);
			if (lookup is ItemLookupResult.Found)
			{
				return IncrementalApplyResult.Applied;
			}

			if (lookup is ItemLookupResult.Ambiguous)
			{
				return IncrementalApplyResult.RequiresFullRefresh;
			}
		}
		finally
		{
			_navigationLock.Release();
		}

		var replacement = await resolver.ResolveAsync(change.CurrentItem!, cancellationToken).ConfigureAwait(false);
		var retained = false;

		try
		{
			if (!HasKey(replacement, key))
			{
				return IncrementalApplyResult.RequiresFullRefresh;
			}

			await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				if (!IsActiveGeneration(context))
				{
					return IncrementalApplyResult.Stale;
				}

				if (ShouldHideItem(ViewSettings, replacement))
				{
					return IncrementalApplyResult.Applied;
				}

				var projection = Volatile.Read(ref _itemProjection);
				lookup = FindItemIndex(projection, key, out _);
				if (lookup is ItemLookupResult.Found)
				{
					return IncrementalApplyResult.Applied;
				}

				if (lookup is ItemLookupResult.Ambiguous)
				{
					return IncrementalApplyResult.RequiresFullRefresh;
				}

				var changes = projection.Add(replacement);
				if (changes.IsEmpty)
				{
					return IncrementalApplyResult.Applied;
				}

				retained = true;
				PublishItemsChanged(changes);
				OnStateChanged();

				return IncrementalApplyResult.Applied;
			}
			finally
			{
				_navigationLock.Release();
			}
		}
		finally
		{
			if (!retained)
			{
				await replacement.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	private async ValueTask<IncrementalApplyResult> ApplyDeletedAsync(BrowseContextState context, FolderChange change, CancellationToken cancellationToken)
	{
		if (!TryGetKey(change.PreviousItem, out var key))
		{
			return IncrementalApplyResult.RequiresFullRefresh;
		}

		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!IsActiveGeneration(context))
			{
				return IncrementalApplyResult.Stale;
			}

			var lookup = FindItemIndex(Volatile.Read(ref _itemProjection), key, out _);
			if (lookup is not ItemLookupResult.Found)
			{
				return IncrementalApplyResult.RequiresFullRefresh;
			}
		}
		finally
		{
			_navigationLock.Release();
		}

		await InvalidateAsync([change.PreviousItem], cancellationToken).ConfigureAwait(false);
		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!IsActiveGeneration(context))
			{
				return IncrementalApplyResult.Stale;
			}

			var projection = Volatile.Read(ref _itemProjection);
			var lookup = FindItemIndex(projection, key, out var index);
			if (lookup is not ItemLookupResult.Found)
			{
				return IncrementalApplyResult.RequiresFullRefresh;
			}

			var removed = projection.Items[index];
			var changes = projection.Remove(key);
			if (changes.IsEmpty)
			{
				return IncrementalApplyResult.RequiresFullRefresh;
			}

			try
			{
				_presentationStore.Remove(key);
				PublishItemsChanged(changes);
				RemoveSelectionKey(key);
				OnStateChanged();
			}
			finally
			{
				await removed.DisposeAsync().ConfigureAwait(false);
			}

			return IncrementalApplyResult.Applied;
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	private async ValueTask<IncrementalApplyResult> ApplyRenamedAsync(BrowseContextState context, IBrowseLocationItemResolver resolver, FolderChange change, CancellationToken cancellationToken)
	{
		if (!TryGetKey(change.CurrentItem, out var currentKey))
		{
			return IncrementalApplyResult.RequiresFullRefresh;
		}

		var oldKey = change.PreviousItem is not null && TryGetKey(change.PreviousItem, out var previousKey) ? previousKey : currentKey;
		var previousKeyToReplace = oldKey;
		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (!IsActiveGeneration(context))
			{
				return IncrementalApplyResult.Stale;
			}

			var lookup = FindItemIndex(Volatile.Read(ref _itemProjection), oldKey, out _);
			if (lookup is not ItemLookupResult.Found && oldKey != currentKey)
			{
				previousKeyToReplace = currentKey;
				lookup = FindItemIndex(Volatile.Read(ref _itemProjection), currentKey, out _);
			}

			if (lookup is not ItemLookupResult.Found)
			{
				return IncrementalApplyResult.RequiresFullRefresh;
			}
		}
		finally
		{
			_navigationLock.Release();
		}

		var replacement = await resolver.ResolveAsync(change.CurrentItem!, cancellationToken).ConfigureAwait(false);
		var retained = false;
		var sameInstance = false;

		try
		{
			if (!HasKey(replacement, currentKey))
			{
				return IncrementalApplyResult.RequiresFullRefresh;
			}

			await InvalidateAsync([change.PreviousItem, change.CurrentItem], cancellationToken).ConfigureAwait(false);

			await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				if (!IsActiveGeneration(context))
				{
					return IncrementalApplyResult.Stale;
				}

				var projection = Volatile.Read(ref _itemProjection);
				var lookup = FindItemIndex(projection, previousKeyToReplace, out var index);

				if (lookup is not ItemLookupResult.Found)
				{
					return IncrementalApplyResult.RequiresFullRefresh;
				}

				if (ShouldHideItem(ViewSettings, replacement))
				{
					return IncrementalApplyResult.RequiresFullRefresh;
				}

				var previous = projection.Items[index];
				if (ReferenceEquals(previous, replacement))
				{
					sameInstance = true;

					return IncrementalApplyResult.RequiresFullRefresh;
				}

				var changes = projection.Replace(previousKeyToReplace, replacement);
				retained = true;
				try
				{
					_presentationStore.Remove(previousKeyToReplace);
					if (previousKeyToReplace != currentKey)
					{
						_presentationStore.Remove(currentKey);
					}

					PublishItemsChanged(changes);
					MigrateSelection(previousKeyToReplace, currentKey);
					OnStateChanged();
				}
				finally
				{
					await previous.DisposeAsync().ConfigureAwait(false);
				}

				return IncrementalApplyResult.Applied;
			}
			finally
			{
				_navigationLock.Release();
			}
		}
		finally
		{
			if (!retained && !sameInstance)
			{
				await replacement.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	private async ValueTask<IncrementalApplyResult> ApplyUpdatedAsync(BrowseContextState context, IBrowseLocationItemResolver resolver, FolderChange change, CancellationToken cancellationToken)
	{
		if (!TryGetKey(change.CurrentItem, out var key))
		{
			return IncrementalApplyResult.RequiresFullRefresh;
		}

		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!IsActiveGeneration(context))
			{
				return IncrementalApplyResult.Stale;
			}

			var lookup = FindItemIndex(Volatile.Read(ref _itemProjection), key, out _);
			if (lookup is not ItemLookupResult.Found)
			{
				return IncrementalApplyResult.RequiresFullRefresh;
			}
		}
		finally
		{
			_navigationLock.Release();
		}

		var replacement = await resolver.ResolveAsync(change.CurrentItem!, cancellationToken).ConfigureAwait(false);
		var retained = false;
		var sameInstance = false;

		try
		{
			if (!HasKey(replacement, key))
			{
				return IncrementalApplyResult.RequiresFullRefresh;
			}

			await InvalidateAsync([change.CurrentItem], cancellationToken).ConfigureAwait(false);
			await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

			try
			{
				if (!IsActiveGeneration(context))
				{
					return IncrementalApplyResult.Stale;
				}

				var projection = Volatile.Read(ref _itemProjection);
				var lookup = FindItemIndex(projection, key, out var index);
				if (lookup is not ItemLookupResult.Found)
				{
					return IncrementalApplyResult.RequiresFullRefresh;
				}

				if (ShouldHideItem(ViewSettings, replacement))
				{
					return IncrementalApplyResult.RequiresFullRefresh;
				}

				var previous = projection.Items[index];
				if (ReferenceEquals(previous, replacement))
				{
					sameInstance = true;

					return IncrementalApplyResult.RequiresFullRefresh;
				}

				var changes = projection.Replace(key, replacement);
				retained = true;
				try
				{
					_presentationStore.Remove(key);
					PublishItemsChanged(changes);
					OnStateChanged();
				}
				finally
				{
					await previous.DisposeAsync().ConfigureAwait(false);
				}

				return IncrementalApplyResult.Applied;
			}
			finally
			{
				_navigationLock.Release();
			}
		}
		finally
		{
			if (!retained && !sameInstance)
			{
				await replacement.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	private async ValueTask InvalidateAsync(IEnumerable<StorableReference?> references, CancellationToken cancellationToken)
	{
		if (_thumbnailCache is null)
		{
			return;
		}

		var seen = new HashSet<StorableKey>();
		foreach (var reference in references)
		{
			if (reference is null || !seen.Add(ToKey(reference)))
			{
				continue;
			}

			await _thumbnailCache.InvalidateAsync(reference, cancellationToken).ConfigureAwait(false);
		}
	}

	private bool IsActiveGeneration(BrowseContextState context)
	{
		return !Volatile.Read(ref _isDisposed) && ReferenceEquals(Volatile.Read(ref _activeContext), context);
	}

	private static bool TryGetKey(StorableReference? reference, out StorableKey key)
	{
		if (reference is null)
		{
			key = default;

			return false;
		}

		key = ToKey(reference);

		return true;
	}

	private static StorableKey ToKey(StorableReference reference)
	{
		return new StorableKey(reference.SourceId, reference.ItemId);
	}

	private static bool HasKey(IStorableModel model, StorableKey key)
	{
		return ToKey(model.Reference) == key;
	}

	private static bool ShouldHideItem(BrowseViewSettings settings, IStorableModel item)
	{
		return !settings.ShowHiddenItems && item.IsHidden;
	}

	private static ItemLookupResult FindItemIndex(BrowseItemProjection projection, StorableKey key, out int index)
	{
		ArgumentNullException.ThrowIfNull(projection);

		return projection.TryGet(key, out _, out index)
			? ItemLookupResult.Found
			: ItemLookupResult.Missing;
	}

	private bool EnqueueChange(BrowseContextState context, FolderChange change)
	{
		if (Volatile.Read(ref _isDisposed) || !IsKnownContext(context))
		{
			return false;
		}

		var pendingChange = new BrowseQueuedChange(context.Generation, change);
		if (!_changeCoordinator.TryEnqueue(pendingChange))
		{
			return RequestFullRefresh(context.Generation);
		}

		return true;
	}

	private void OnFolderChanged(BrowseContextState context, FolderChange change)
	{
		EnqueueChange(context, change);
	}

	private void OnFolderChangeFaulted(BrowseContextState context, FolderChangeErrorEventArgs args)
	{
		if (!RequestFullRefresh(context.Generation))
		{
			return;
		}

		Error = args.Error;
		OnStateChanged();
	}

	private bool RequestFullRefresh(long generation)
	{
		if (Volatile.Read(ref _isDisposed))
		{
			return false;
		}

		var activeGeneration = Volatile.Read(ref _activeContext)?.Generation;
		var preparingGeneration = Volatile.Read(ref _preparingContext)?.Generation;
		if (activeGeneration != generation && preparingGeneration != generation)
		{
			return false;
		}

		return _changeCoordinator.RequestFullRefresh(generation);
	}

	private bool IsKnownContext(BrowseContextState context)
	{
		return ReferenceEquals(Volatile.Read(ref _activeContext), context) || ReferenceEquals(Volatile.Read(ref _preparingContext), context);
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

			var previousLayoutMode = ViewSettings.LayoutMode;
			var changes = Volatile.Read(ref _itemProjection).UpdateSort(settings);
			ViewSettings = settings;
			if (previousLayoutMode != settings.LayoutMode)
			{
				clearedThumbnails = _presentationStore.ClearThumbnails();
			}

			PublishItemsChanged(changes, contentChanged: false);
			OnStateChanged();
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
	public bool TryGetPresentation(StorableKey key, out BrowseItemPresentation presentation)
	{
		return _presentationStore.TryGet(key, out presentation);
	}

	async ValueTask<bool> IBrowsePrefetchTarget.PublishPropertiesAsync(
		long generation,
		long expectedContentVersion,
		IStorableModel item,
		IReadOnlyDictionary<string, object?> properties,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(item);
		ArgumentNullException.ThrowIfNull(properties);

		BrowseItemPresentationChangedEventArgs? presentationChanged = null;
		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!TryValidatePrefetchItem(generation, expectedContentVersion, item, out var key))
			{
				return false;
			}

			var presentation = _presentationStore.Update(key, item, properties, thumbnail: null, updateProperties: true, updateThumbnail: false);
			presentationChanged = new BrowseItemPresentationChangedEventArgs(key, presentation, BrowseItemPresentationChangeFlags.Properties);

			if (!string.IsNullOrWhiteSpace(ViewSettings.SortPropertyId) && properties.ContainsKey(ViewSettings.SortPropertyId))
			{
				SchedulePropertySort(generation, expectedContentVersion);
			}
		}
		finally
		{
			_navigationLock.Release();
		}

		RaiseEvent(ItemPresentationChanged, presentationChanged!);

		return true;
	}

	async ValueTask<bool> IBrowsePrefetchTarget.PublishThumbnailAsync(long generation, long expectedContentVersion, IStorableModel item, ThumbnailResult thumbnail, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(item);
		ArgumentNullException.ThrowIfNull(thumbnail);

		BrowseItemPresentationChangedEventArgs? presentationChanged = null;
		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (!TryValidatePrefetchItem(generation, expectedContentVersion, item, out var key))
			{
				return false;
			}

			var presentation = _presentationStore.Update(key, item, properties: null, thumbnail: thumbnail, updateProperties: false, updateThumbnail: true);
			presentationChanged = new BrowseItemPresentationChangedEventArgs(key, presentation, BrowseItemPresentationChangeFlags.Thumbnail);
		}
		finally
		{
			_navigationLock.Release();
		}

		RaiseEvent(ItemPresentationChanged, presentationChanged!);

		return true;
	}

	private bool TryValidatePrefetchItem(long generation, long expectedContentVersion, IStorableModel item, out StorableKey key)
	{
		key = item.Reference.GetKey();
		if (Generation != generation || Volatile.Read(ref _contentVersion) != expectedContentVersion)
		{
			return false;
		}

		return Volatile.Read(ref _itemProjection).TryGet(key, out var current)
			&& ReferenceEquals(current, item);
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

	private void SchedulePropertySort(long generation, long contentVersion)
	{
		CancellationTokenSource? previousCancellation;
		CancellationTokenSource cancellation;
		lock (_propertySortLock)
		{
			previousCancellation = _propertySortCancellation;
			cancellation = new CancellationTokenSource();
			_propertySortCancellation = cancellation;
			_propertySortTask = ApplyPropertySortAsync(generation, contentVersion, cancellation);
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

	private async Task ApplyPropertySortAsync(long generation, long contentVersion, CancellationTokenSource cancellation)
	{
		try
		{
			await Task.Delay(PropertySortDebounce, cancellation.Token).ConfigureAwait(false);
			await _navigationLock.WaitAsync(cancellation.Token).ConfigureAwait(false);
			try
			{
				if (Volatile.Read(ref _isDisposed) || Generation != generation || Volatile.Read(ref _contentVersion) != contentVersion)
				{
					return;
				}

				var changes = Volatile.Read(ref _itemProjection).RefreshSort();
				PublishItemsChanged(changes, contentChanged: false);
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

	private void PublishItemsChanged(BrowseItemChangeSet changeSet, bool contentChanged = true)
	{
		if (changeSet.IsEmpty)
		{
			return;
		}

		if (contentChanged)
		{
			Interlocked.Increment(ref _contentVersion);
		}

		var previousVersion = Interlocked.Read(ref _itemsVersion);
		var version = Interlocked.Increment(ref _itemsVersion);
		var eventStartTimestamp = Stopwatch.GetTimestamp();
		CoreDiagnosticLog.Write("BrowseSession", $"ItemsChanged START version={version} previous={previousVersion} changes={changeSet.Changes.Count} contentChanged={contentChanged} items={Items.Count}");
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

	private sealed record BrowseNavigationSnapshot(
		BrowseLocation? Location,
		BrowseContextState? ActiveContext,
		BrowseItemProjection ItemProjection,
		BrowseViewSettings ViewSettings,
		BrowseSelectionState Selection,
		IReadOnlyList<IStorableModel> Items,
		BrowsePresentationStore.Snapshot Presentations);

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
