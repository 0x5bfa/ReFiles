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

/// <summary>Coordinates navigation, item enumeration, selection, and presentation for one browse tab.</summary>
public sealed class BrowseSession : IBrowseSession, IBrowsePrefetchTarget, IInteractiveBrowseSession
{
	private const int InitialEnumerationBatchSize = 32;
	private const int SearchInitialEnumerationBatchSize = 1;
	private const int EnumerationBatchSize = 256;
	private const int MaximumEnumerationBatchSize = 1024;
	private static readonly TimeSpan PropertySortDebounce = TimeSpan.FromMilliseconds(150);

	private readonly IBrowseLocationResolver _locationResolver;
	private readonly IViewSettingsStore? _viewSettingsStore;
	private readonly IThumbnailCache? _thumbnailCache;
	private readonly Dictionary<ViewSettingsScopeKey, BrowseViewSettingsOverride> _sessionViewSettings = [];
	private readonly Dictionary<BrowseLocation, BrowseViewSettingsOverride> _unscopedSessionViewSettings = [];
	private BrowseItemProjection _itemProjection;
	private readonly SemaphoreSlim _navigationLock = new(1, 1);
	private readonly BrowseChangeCoordinator _changeCoordinator;
	private readonly Lock _disposalLock = new();
	private readonly Lock _navigationCancellationLock = new();
	private readonly Lock _propertySortLock = new();
	private readonly HashSet<long> _suppressedPropertySortGenerations = [];
	private readonly BrowsePresentationStore _presentationStore = new();
	private readonly BrowseSelectionModel _selectionModel = new();
	private BrowseContextState? _activeContext;
	private BrowseContextState? _preparingContext;
	private ViewSettingsScopeKey? _viewSettingsScope;
	private BrowseViewSettings _viewSettingsBaseline = BrowseViewSettings.Default;
	private BrowseViewSettingsOverride _providerViewSettings = new(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
	private BrowseViewSettingsOverride _viewSettingsOverride = new(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
	private Task? _disposeTask;
	private CancellationTokenSource? _activeNavigationCancellation;
	private CancellationTokenSource? _propertySortCancellation;
	private Task? _propertySortTask;
	private int _navigationOperationsInFlight;
	private long _propertySortGeneration;
	private long _generationCounter;
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
	public bool TryGet(StorableKey key, out IStorableModel item) => Volatile.Read(ref _itemProjection).TryGet(key, out item!);

	/// <inheritdoc />
	public long ItemsVersion => Volatile.Read(ref _itemsVersion);

	/// <inheritdoc />
	public BrowseSelectionState Selection => _selectionModel.State;

	/// <inheritdoc />
	public BrowseViewSettings ViewSettings { get; private set; }

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
		if (navigation.PendingPropertySortTask is not null)
		{
			await navigation.PendingPropertySortTask.ConfigureAwait(false);
		}

		await _navigationLock.WaitAsync(navigation.Token).ConfigureAwait(false);

		try
		{
			await NavigateCoreAsync(location, ownerWindowHandle, navigation.Token).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	private async ValueTask NavigateCoreAsync(BrowseLocation location, nint ownerWindowHandle, CancellationToken cancellationToken)
	{
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

			ViewSettingsScopeKey.TryForLocation(location, out var nextViewSettingsScope);
			BrowseViewSettingsOverride? nextProviderViewSettings;
			BrowseViewSettingsOverride? nextViewSettingsOverride;
			await using (var viewSettingsTransactionLock = await ViewSettingsTransactionLock.AcquireAsync(nextViewSettingsScope, cancellationToken).ConfigureAwait(false))
			{
				nextProviderViewSettings = await GetProviderViewSettingsAsync(nextLocationContext, cancellationToken).ConfigureAwait(false);
				nextViewSettingsOverride = nextViewSettingsScope is null
					? _unscopedSessionViewSettings.GetValueOrDefault(location)
					: _viewSettingsStore is null
						? _sessionViewSettings.GetValueOrDefault(nextViewSettingsScope)
						: await _viewSettingsStore.GetAsync(nextViewSettingsScope, cancellationToken).ConfigureAwait(false);
			}

			nextProviderViewSettings ??= new BrowseViewSettingsOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
			nextViewSettingsOverride ??= new BrowseViewSettingsOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
			var nextViewSettingsBaseline = BrowseViewSettings.Default;
			var nextViewSettings = ApplyViewSettingsLayers(nextViewSettingsBaseline, nextProviderViewSettings, nextViewSettingsOverride);

			await nextContext.StartAsync(cancellationToken).ConfigureAwait(false);
			nextProjection = new BrowseItemProjection(nextViewSettings, _presentationStore.GetSortPropertyValue);
			var targetBatchSize = location is SearchLocation ? SearchInitialEnumerationBatchSize : InitialEnumerationBatchSize;
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
				PublishEnumerationBatch(location, nextViewSettingsScope, nextViewSettingsBaseline, nextProviderViewSettings, nextViewSettingsOverride, nextViewSettings, nextContext, nextProjection,
					batchToPublish,
					ref previousState,
					ref enumerationActivated);
				pendingBatch.Clear();
				targetBatchSize = targetBatchSize switch
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
				PublishEnumerationBatch(location, nextViewSettingsScope, nextViewSettingsBaseline, nextProviderViewSettings, nextViewSettingsOverride, nextViewSettings, nextContext, nextProjection, finalBatch,
					ref previousState,
					ref enumerationActivated);
			}
			else if (!enumerationActivated)
			{
				PublishEnumerationBatch(location, nextViewSettingsScope, nextViewSettingsBaseline, nextProviderViewSettings, nextViewSettingsOverride, nextViewSettings, nextContext, nextProjection, [],
					ref previousState,
					ref enumerationActivated);
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
					if (previousState.ActiveContext is not null)
					{
						RequestFullRefresh(previousState.ActiveContext.Generation);
					}
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
		ViewSettingsScopeKey? viewSettingsScope,
		BrowseViewSettings viewSettingsBaseline,
		BrowseViewSettingsOverride providerViewSettings,
		BrowseViewSettingsOverride viewSettingsOverride,
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
			var currentProjection = Volatile.Read(ref _itemProjection);
			currentProjection.InvokeLocked(() =>
			{
				Location = location;
				Volatile.Write(ref _activeContext, context);
				_viewSettingsScope = viewSettingsScope;
				_viewSettingsBaseline = viewSettingsBaseline;
				_providerViewSettings = providerViewSettings;
				_viewSettingsOverride = viewSettingsOverride;
				ViewSettings = settings;
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

	private BrowseNavigationSnapshot CaptureNavigationState()
	{
		return new BrowseNavigationSnapshot(Location, Volatile.Read(ref _activeContext), Volatile.Read(ref _itemProjection), _viewSettingsScope, _viewSettingsBaseline, _providerViewSettings,
			_viewSettingsOverride, ViewSettings, Selection, Items, _presentationStore.Capture());
	}

	private void RestoreNavigationState(BrowseNavigationSnapshot previousState)
	{
		ArgumentNullException.ThrowIfNull(previousState);

		var currentProjection = Volatile.Read(ref _itemProjection);
		currentProjection.InvokeLocked(() =>
		{
			Location = previousState.Location;
			Volatile.Write(ref _activeContext, previousState.ActiveContext);
			Volatile.Write(ref _preparingContext, null);
			Volatile.Write(ref _itemProjection, previousState.ItemProjection);
			_viewSettingsScope = previousState.ViewSettingsScope;
			_viewSettingsBaseline = previousState.ViewSettingsBaseline;
			_providerViewSettings = previousState.ProviderViewSettings;
			_viewSettingsOverride = previousState.ViewSettingsOverride;
			ViewSettings = previousState.ViewSettings;
			_presentationStore.Restore(previousState.Presentations);
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
			if (HasNavigationOperationsInFlight() || !ReferenceEquals(currentContext, Volatile.Read(ref _activeContext)))
			{
				return true;
			}

			_changeCoordinator.TryClearFullRefresh(generation);

			return false;
		}

		if (generation < currentContext.Generation)
		{
			if (HasNavigationOperationsInFlight() || !ReferenceEquals(currentContext, Volatile.Read(ref _activeContext)))
			{
				return true;
			}

			_changeCoordinator.TryClearFullRefresh(generation);

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

			IncrementalApplyResult result;
			try
			{
				result = await ApplyChangeAsync(pendingChange, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				RequestFullRefresh(currentContext.Generation);

				return;
			}

			if (result is IncrementalApplyResult.RequiresFullRefresh)
			{
				RequestFullRefresh(currentContext.Generation);

				return;
			}
		}
	}

	private async ValueTask RefreshCurrentAsync(long generation, CancellationToken cancellationToken)
	{
		using var navigation = TryBeginNavigation(generation, cancellationToken);
		if (navigation is null)
		{
			return;
		}

		if (navigation.PendingPropertySortTask is not null)
		{
			await navigation.PendingPropertySortTask.ConfigureAwait(false);
		}

		await _navigationLock.WaitAsync(navigation.Token).ConfigureAwait(false);

		try
		{
			var currentContext = Volatile.Read(ref _activeContext);
			if (currentContext is null || currentContext.Generation != generation)
			{
				return;
			}

			try
			{
				await NavigateCoreAsync(currentContext.Context.Location, 0, navigation.Token).ConfigureAwait(false);
				_changeCoordinator.TryClearFullRefresh(generation);
			}
			catch (OperationCanceledException) when (!navigation.Token.IsCancellationRequested || cancellationToken.IsCancellationRequested)
			{
				_changeCoordinator.TryClearFullRefresh(generation);

				throw;
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				if (!navigation.Token.IsCancellationRequested || cancellationToken.IsCancellationRequested)
				{
					_changeCoordinator.TryClearFullRefresh(generation);
				}

				throw;
			}
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

				if (ShouldHideItem(DisplaySettings, replacement))
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

				if (ShouldHideItem(DisplaySettings, replacement))
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

				if (ShouldHideItem(DisplaySettings, replacement))
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

	private static bool ShouldHideItem(BrowseDisplaySettings settings, IStorableModel item)
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

		if (!IsKnownGeneration(generation))
		{
			return false;
		}

		return _changeCoordinator.RequestFullRefresh(generation);
	}

	private bool IsKnownGeneration(long generation)
	{
		return Volatile.Read(ref _activeContext)?.Generation == generation || Volatile.Read(ref _preparingContext)?.Generation == generation;
	}

	private bool IsKnownContext(BrowseContextState context)
	{
		return ReferenceEquals(Volatile.Read(ref _activeContext), context) || ReferenceEquals(Volatile.Read(ref _preparingContext), context);
	}

	/// <inheritdoc />
	public ValueTask UpdateViewSettingsAsync(BrowseViewSettings settings, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);
		ArgumentNullException.ThrowIfNull(settings);

		return UpdateViewSettingsAsync(BrowseViewSettingsOverride.FromSettings(settings), cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask UpdateViewSettingsAsync(BrowseViewSettingsOverride settingsOverride, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);
		ArgumentNullException.ThrowIfNull(settingsOverride);

		var expectedGeneration = Generation;
		var previewSettings = settingsOverride.ApplyTo(ViewSettings);
		using var propertySortRestorer = new PropertySortRestorer(this, expectedGeneration);
		var propertySortTask = SortSettingsDiffer(ViewSettings, previewSettings) ? CancelPendingPropertySort(expectedGeneration) : null;
		if (propertySortTask is not null)
		{
			propertySortRestorer.MarkCanceled();
			await propertySortTask.ConfigureAwait(false);
		}

		BrowseItemPresentationChangedEventArgs[] clearedThumbnails = [];
		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (Generation != expectedGeneration)
			{
				return;
			}

			if (Location is null)
			{
				throw new InvalidOperationException("View settings require an active browse location.");
			}

			var requestedSettings = settingsOverride.ApplyTo(ViewSettings);
			var sortSettingsChanged = SortSettingsDiffer(ViewSettings, requestedSettings);
			propertySortTask = sortSettingsChanged ? CancelPendingPropertySort(expectedGeneration) : null;
			if (propertySortTask is not null)
			{
				propertySortRestorer.MarkCanceled();
				await propertySortTask.ConfigureAwait(false);
			}

			cancellationToken.ThrowIfCancellationRequested();

			var preparedUpdate = await PrepareEffectiveViewSettingsAsync(requestedSettings, cancellationToken).ConfigureAwait(false);
			BrowseViewSettingsOverride nextProviderViewSettings;
			BrowseViewSettingsOverride nextApplicationOverride;
			BrowseViewSettings effectiveSettings;
			await using (var viewSettingsTransactionLock = await ViewSettingsTransactionLock.AcquireAsync(_viewSettingsScope, cancellationToken).ConfigureAwait(false))
			{
				var currentProviderViewSettings = Context is null ? null : await GetProviderViewSettingsAsync(Context, cancellationToken).ConfigureAwait(false);
				currentProviderViewSettings ??= _providerViewSettings;
				var currentApplicationViewSettings = await GetApplicationViewSettingsAsync(Location, _viewSettingsScope, cancellationToken).ConfigureAwait(false);
				var currentEffectiveSettings = ApplyViewSettingsLayers(_viewSettingsBaseline, currentProviderViewSettings, currentApplicationViewSettings);
				var transactionRequestedSettings = settingsOverride.ApplyTo(currentEffectiveSettings);
				var requestedColumnMode = settingsOverride.ColumnMode;
				if (settingsOverride.Fields.HasFlag(ViewSettingsOverrideFields.DetailsColumns) && settingsOverride.ColumnMode is ViewColumnSettingsMode.Insert &&
					currentApplicationViewSettings.Fields.HasFlag(ViewSettingsOverrideFields.DetailsColumns) && currentApplicationViewSettings.ColumnMode is ViewColumnSettingsMode.Replace)
				{
					requestedColumnMode = ViewColumnSettingsMode.Replace;
				}

				var transactionRequestedOverride = new BrowseViewSettingsOverride(settingsOverride.Fields, transactionRequestedSettings, requestedColumnMode);
				var fallbackApplicationOverride = await PatchApplicationViewSettingsAsync(
					Location, _viewSettingsScope, settingsOverride.Fields, transactionRequestedOverride, cancellationToken).ConfigureAwait(false);
				var completionToken = CancellationToken.None;
				var persistenceResult = await PersistProviderViewSettingsAsync(transactionRequestedOverride, completionToken).ConfigureAwait(false);
				nextProviderViewSettings = persistenceResult.ProviderSettings ?? currentProviderViewSettings;
				var applicationReplacement = persistenceResult.ApplicationSettings;
				if ((applicationReplacement.Fields & ~settingsOverride.Fields) != ViewSettingsOverrideFields.None)
				{
					CoreDiagnosticLog.Write("BrowseSession", "Provider view settings persistence returned application settings outside the requested fields.");
					applicationReplacement = transactionRequestedOverride;
				}

				var candidateApplicationOverride = fallbackApplicationOverride.ReplaceFields(settingsOverride.Fields, applicationReplacement);
				var candidateSettings = ApplyViewSettingsLayers(_viewSettingsBaseline, nextProviderViewSettings, candidateApplicationOverride);
				if (!ViewSettingsAreEquivalentForFields(candidateSettings, transactionRequestedSettings, settingsOverride.Fields))
				{
					applicationReplacement = transactionRequestedOverride;
				}

				try
				{
					nextApplicationOverride = await PatchApplicationViewSettingsAsync(Location, _viewSettingsScope, settingsOverride.Fields, applicationReplacement, completionToken).ConfigureAwait(false);
				}
				catch (Exception exception)
				{
					CoreDiagnosticLog.Write("BrowseSession", $"Application view settings compaction failed type={exception.GetType().Name} message={exception.Message}");
					nextApplicationOverride = fallbackApplicationOverride;
				}

				effectiveSettings = ApplyViewSettingsLayers(_viewSettingsBaseline, nextProviderViewSettings, nextApplicationOverride);
			}

			sortSettingsChanged = SortSettingsDiffer(ViewSettings, effectiveSettings);
			if (!ViewSettingsAreEquivalent(effectiveSettings, requestedSettings))
			{
				propertySortTask = sortSettingsChanged ? CancelPendingPropertySort(expectedGeneration) : null;
				if (propertySortTask is not null)
				{
					propertySortRestorer.MarkCanceled();
					await propertySortTask.ConfigureAwait(false);
				}

				preparedUpdate = await PreparePersistedEffectiveViewSettingsAsync(effectiveSettings, CancellationToken.None).ConfigureAwait(false);
			}

			clearedThumbnails = CommitEffectiveViewSettings(effectiveSettings, preparedUpdate);
			_providerViewSettings = nextProviderViewSettings;
			_viewSettingsOverride = nextApplicationOverride;
			if (sortSettingsChanged)
			{
				propertySortRestorer.MarkReplaced();
			}
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
	public async ValueTask<bool> TryApplyViewSettingsBaselineAsync(BrowseLocation expectedLocation, long expectedGeneration, BrowseViewSettings settings, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);
		ArgumentNullException.ThrowIfNull(expectedLocation);
		ArgumentNullException.ThrowIfNull(settings);

		if (Generation != expectedGeneration || !Equals(Location, expectedLocation))
		{
			return false;
		}

		var previewSettings = ApplyViewSettingsLayers(settings, _providerViewSettings, _viewSettingsOverride);
		using var propertySortRestorer = new PropertySortRestorer(this, expectedGeneration);
		var propertySortTask = SortSettingsDiffer(ViewSettings, previewSettings) ? CancelPendingPropertySort(expectedGeneration) : null;
		if (propertySortTask is not null)
		{
			propertySortRestorer.MarkCanceled();
			await propertySortTask.ConfigureAwait(false);
		}

		BrowseItemPresentationChangedEventArgs[] clearedThumbnails = [];
		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (Generation != expectedGeneration || !Equals(Location, expectedLocation))
			{
				return false;
			}

			var effectiveSettings = ApplyViewSettingsLayers(settings, _providerViewSettings, _viewSettingsOverride);
			var sortSettingsChanged = SortSettingsDiffer(ViewSettings, effectiveSettings);
			propertySortTask = sortSettingsChanged ? CancelPendingPropertySort(expectedGeneration) : null;
			if (propertySortTask is not null)
			{
				propertySortRestorer.MarkCanceled();
				await propertySortTask.ConfigureAwait(false);
			}

			cancellationToken.ThrowIfCancellationRequested();

			var preparedUpdate = await PrepareEffectiveViewSettingsAsync(effectiveSettings, cancellationToken).ConfigureAwait(false);
			clearedThumbnails = CommitEffectiveViewSettings(effectiveSettings, preparedUpdate);
			_viewSettingsBaseline = settings;
			if (sortSettingsChanged)
			{
				propertySortRestorer.MarkReplaced();
			}
		}
		finally
		{
			_navigationLock.Release();
		}

		foreach (var thumbnail in clearedThumbnails)
		{
			RaiseEvent(ItemPresentationChanged, thumbnail);
		}

		return true;
	}

	/// <inheritdoc />
	public async ValueTask ResetViewSettingsAsync(CancellationToken cancellationToken = default)
	{
		await ClearViewSettingsAsync(ViewSettingsOverrideFields.All, clearProvider: true, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public ValueTask ClearViewSettingsOverridesAsync(ViewSettingsOverrideFields fields, CancellationToken cancellationToken = default)
	{
		return ClearViewSettingsAsync(fields, clearProvider: false, cancellationToken);
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

			var presentation = _presentationStore.UpdateProperties(key, item, properties);
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

			if (!_presentationStore.TryUpdateThumbnail(key, item, thumbnail, out var presentation))
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

	private async ValueTask ClearViewSettingsAsync(ViewSettingsOverrideFields fields, bool clearProvider, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);

		if ((fields & ~ViewSettingsOverrideFields.All) != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(fields));
		}

		if (fields == ViewSettingsOverrideFields.None)
		{
			return;
		}

		var expectedGeneration = Generation;
		var currentOverride = _viewSettingsOverride;
		var previewRetainedFields = currentOverride.Fields & ~fields;
		var previewColumnMode = previewRetainedFields.HasFlag(ViewSettingsOverrideFields.DetailsColumns) ? currentOverride.ColumnMode : ViewColumnSettingsMode.Replace;
		var previewOverride = new BrowseViewSettingsOverride(previewRetainedFields, currentOverride.Values, previewColumnMode);
		var previewProvider = clearProvider ? new BrowseViewSettingsOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default) : _providerViewSettings;
		var previewSettings = ApplyViewSettingsLayers(_viewSettingsBaseline, previewProvider, previewOverride);
		using var propertySortRestorer = new PropertySortRestorer(this, expectedGeneration);
		var propertySortTask = SortSettingsDiffer(ViewSettings, previewSettings) ? CancelPendingPropertySort(expectedGeneration) : null;
		if (propertySortTask is not null)
		{
			propertySortRestorer.MarkCanceled();
			await propertySortTask.ConfigureAwait(false);
		}

		BrowseItemPresentationChangedEventArgs[] clearedThumbnails = [];
		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (Generation != expectedGeneration)
			{
				return;
			}

			if (Location is null)
			{
				throw new InvalidOperationException("View settings require an active browse location.");
			}

			var retainedFields = _viewSettingsOverride.Fields & ~fields;
			var retainedColumnMode = retainedFields.HasFlag(ViewSettingsOverrideFields.DetailsColumns) ? _viewSettingsOverride.ColumnMode : ViewColumnSettingsMode.Replace;
			var retainedOverride = new BrowseViewSettingsOverride(retainedFields, _viewSettingsOverride.Values, retainedColumnMode);
			var expectedProviderViewSettings = clearProvider ? RemoveOverrideFields(_providerViewSettings, fields) : _providerViewSettings;
			var effectiveSettings = ApplyViewSettingsLayers(_viewSettingsBaseline, expectedProviderViewSettings, retainedOverride);
			var sortSettingsChanged = SortSettingsDiffer(ViewSettings, effectiveSettings);
			propertySortTask = sortSettingsChanged ? CancelPendingPropertySort(expectedGeneration) : null;
			if (propertySortTask is not null)
			{
				propertySortRestorer.MarkCanceled();
				await propertySortTask.ConfigureAwait(false);
			}

			cancellationToken.ThrowIfCancellationRequested();

			var preparedSettings = effectiveSettings;
			var preparedUpdate = await PrepareEffectiveViewSettingsAsync(effectiveSettings, cancellationToken).ConfigureAwait(false);
			var completionToken = cancellationToken;
			BrowseViewSettingsOverride nextProviderViewSettings;
			await using (var viewSettingsTransactionLock = await ViewSettingsTransactionLock.AcquireAsync(_viewSettingsScope, cancellationToken).ConfigureAwait(false))
			{
				var currentProviderViewSettings = Context is null ? null : await GetProviderViewSettingsAsync(Context, cancellationToken).ConfigureAwait(false);
				currentProviderViewSettings ??= _providerViewSettings;
				expectedProviderViewSettings = clearProvider ? RemoveOverrideFields(currentProviderViewSettings, fields) : currentProviderViewSettings;
				effectiveSettings = ApplyViewSettingsLayers(_viewSettingsBaseline, expectedProviderViewSettings, retainedOverride);
				nextProviderViewSettings = expectedProviderViewSettings;
				var providerClearSucceeded = true;
				BrowseViewSettingsOverride? providerClearFallback = null;
				if (clearProvider)
				{
					var fallbackOverride = new BrowseViewSettingsOverride(fields, effectiveSettings);
					providerClearFallback = await PatchApplicationViewSettingsAsync(Location, _viewSettingsScope, fields, fallbackOverride, cancellationToken).ConfigureAwait(false);
					retainedOverride = providerClearFallback;
					completionToken = CancellationToken.None;
					try
					{
						nextProviderViewSettings = await ClearProviderViewSettingsAsync(fields, expectedProviderViewSettings, completionToken).ConfigureAwait(false);
					}
					catch (Exception exception)
					{
						CoreDiagnosticLog.Write("BrowseSession", $"Provider view settings reset failed type={exception.GetType().Name} message={exception.Message}");
						nextProviderViewSettings = currentProviderViewSettings;
						providerClearSucceeded = false;
					}
				}

				if (providerClearSucceeded)
				{
					var emptyOverride = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
					try
					{
						retainedOverride = await PatchApplicationViewSettingsAsync(Location, _viewSettingsScope, fields, emptyOverride, completionToken).ConfigureAwait(false);
					}
					catch (Exception exception) when (providerClearFallback is not null)
					{
						CoreDiagnosticLog.Write("BrowseSession", $"Application view settings reset compaction failed type={exception.GetType().Name} message={exception.Message}");
						retainedOverride = providerClearFallback;
					}
				}

				effectiveSettings = ApplyViewSettingsLayers(_viewSettingsBaseline, nextProviderViewSettings, retainedOverride);
			}

			sortSettingsChanged = SortSettingsDiffer(ViewSettings, effectiveSettings);
			if (!ViewSettingsAreEquivalent(effectiveSettings, preparedSettings))
			{
				propertySortTask = sortSettingsChanged ? CancelPendingPropertySort(expectedGeneration) : null;
				if (propertySortTask is not null)
				{
					propertySortRestorer.MarkCanceled();
					await propertySortTask.ConfigureAwait(false);
				}

				preparedUpdate = await PreparePersistedEffectiveViewSettingsAsync(effectiveSettings, completionToken).ConfigureAwait(false);
			}

			clearedThumbnails = CommitEffectiveViewSettings(effectiveSettings, preparedUpdate);
			_providerViewSettings = nextProviderViewSettings;
			_viewSettingsOverride = retainedOverride;
			if (sortSettingsChanged)
			{
				propertySortRestorer.MarkReplaced();
			}
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

	private async ValueTask<PreparedViewSettingsUpdate> PrepareEffectiveViewSettingsAsync(BrowseViewSettings settings, CancellationToken cancellationToken)
	{
		var sortChanged = !string.Equals(ViewSettings.SortPropertyId, settings.SortPropertyId, StringComparison.Ordinal) || ViewSettings.SortDirection != settings.SortDirection;
		if (!sortChanged)
		{
			return new PreparedViewSettingsUpdate(false, null);
		}

		var context = Volatile.Read(ref _activeContext)!.Context;
		if (context is not IBrowseLocationItemSorter sorter)
		{
			return new PreparedViewSettingsUpdate(true, null);
		}

		var projection = Volatile.Read(ref _itemProjection);
		var currentItems = projection.Items;
		var externalOrder = await sorter.SortItemsAsync(currentItems, settings, cancellationToken).ConfigureAwait(false);
		if (externalOrder is not null)
		{
			ValidateExternalOrder(currentItems, externalOrder);
		}

		return new PreparedViewSettingsUpdate(true, externalOrder);
	}

	private async ValueTask<PreparedViewSettingsUpdate> PreparePersistedEffectiveViewSettingsAsync(BrowseViewSettings settings, CancellationToken cancellationToken)
	{
		try
		{
			return await PrepareEffectiveViewSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			CoreDiagnosticLog.Write("BrowseSession", $"Persisted view settings external sort failed type={exception.GetType().Name} message={exception.Message}");

			return new PreparedViewSettingsUpdate(SortSettingsDiffer(ViewSettings, settings), null);
		}
	}

	private BrowseItemPresentationChangedEventArgs[] CommitEffectiveViewSettings(BrowseViewSettings settings, PreparedViewSettingsUpdate preparedUpdate)
	{
		if (ViewSettings == settings)
		{
			return [];
		}

		var previousLayoutMode = ViewSettings.LayoutMode;
		var projection = Volatile.Read(ref _itemProjection);
		var changes = BrowseItemChangeSet.Empty;
		projection.InvokeLocked(() =>
		{
			if (preparedUpdate.SortChanged)
			{
				changes = projection.UpdateSort(settings, deferSort: preparedUpdate.ExternalOrder is not null);
				if (preparedUpdate.ExternalOrder is not null)
				{
					changes = projection.ApplyExternalOrder(preparedUpdate.ExternalOrder);
				}
			}

			ViewSettings = settings;
		});
		var clearedThumbnails = previousLayoutMode != settings.LayoutMode ? _presentationStore.ClearThumbnails() : [];
		PublishItemsChanged(changes);
		OnStateChanged();

		return clearedThumbnails;
	}

	private static void ValidateExternalOrder(IReadOnlyList<IStorableModel> currentItems, IReadOnlyList<IStorableModel> externalOrder)
	{
		if (currentItems.Count != externalOrder.Count)
		{
			throw new InvalidOperationException("The external order must contain every projected item exactly once.");
		}

		var currentByKey = currentItems.ToDictionary(static item => item.Reference.GetKey());
		var orderedKeys = new HashSet<StorableKey>();
		foreach (var item in externalOrder)
		{
			var key = item.Reference.GetKey();
			if (!orderedKeys.Add(key) || !currentByKey.TryGetValue(key, out var currentItem) || !ReferenceEquals(currentItem, item))
			{
				throw new InvalidOperationException("The external order must contain every projected item exactly once.");
			}
		}
	}

	private static bool SortSettingsDiffer(BrowseViewSettings current, BrowseViewSettings next)
	{
		return !string.Equals(current.SortPropertyId, next.SortPropertyId, StringComparison.Ordinal) || current.SortDirection != next.SortDirection;
	}

	private static bool ViewSettingsAreEquivalent(BrowseViewSettings current, BrowseViewSettings next)
	{
		return current.LayoutMode == next.LayoutMode &&
			current.Columns.SequenceEqual(next.Columns) &&
			string.Equals(current.SortPropertyId, next.SortPropertyId, StringComparison.Ordinal) &&
			current.SortDirection == next.SortDirection &&
			current.ItemSize == next.ItemSize &&
			string.Equals(current.GroupPropertyId, next.GroupPropertyId, StringComparison.Ordinal) &&
			current.GroupDirection == next.GroupDirection;
	}

	private static bool ViewSettingsAreEquivalentForFields(BrowseViewSettings current, BrowseViewSettings next, ViewSettingsOverrideFields fields)
	{
		return (!fields.HasFlag(ViewSettingsOverrideFields.LayoutMode) || current.LayoutMode == next.LayoutMode) &&
			(!fields.HasFlag(ViewSettingsOverrideFields.DetailsColumns) || current.Columns.SequenceEqual(next.Columns)) &&
			(!fields.HasFlag(ViewSettingsOverrideFields.SortPropertyId) || string.Equals(current.SortPropertyId, next.SortPropertyId, StringComparison.Ordinal)) &&
			(!fields.HasFlag(ViewSettingsOverrideFields.SortDirection) || current.SortDirection == next.SortDirection) &&
			(!fields.HasFlag(ViewSettingsOverrideFields.ItemSize) || current.ItemSize == next.ItemSize) &&
			(!fields.HasFlag(ViewSettingsOverrideFields.GroupPropertyId) || string.Equals(current.GroupPropertyId, next.GroupPropertyId, StringComparison.Ordinal)) &&
			(!fields.HasFlag(ViewSettingsOverrideFields.GroupDirection) || current.GroupDirection == next.GroupDirection);
	}

	private static BrowseViewSettings ApplyViewSettingsLayers(BrowseViewSettings baseline, BrowseViewSettingsOverride providerSettings, BrowseViewSettingsOverride applicationSettings)
	{
		return applicationSettings.ApplyTo(providerSettings.ApplyTo(baseline));
	}

	private async ValueTask<ViewSettingsPersistenceResult> PersistProviderViewSettingsAsync(BrowseViewSettingsOverride settingsOverride, CancellationToken cancellationToken)
	{
		if (Context is not IViewSettingsPersistenceProvider provider)
		{
			return new ViewSettingsPersistenceResult(null, settingsOverride);
		}

		try
		{
			return await provider.SetViewSettingsAsync(settingsOverride, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			CoreDiagnosticLog.Write("BrowseSession", $"Provider view settings persistence failed type={exception.GetType().Name} message={exception.Message}");

			return new ViewSettingsPersistenceResult(null, settingsOverride);
		}
	}

	private async ValueTask<BrowseViewSettingsOverride> ClearProviderViewSettingsAsync(ViewSettingsOverrideFields fields, BrowseViewSettingsOverride fallbackSettings, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(fallbackSettings);

		if (Context is not IViewSettingsPersistenceProvider provider)
		{
			return fallbackSettings;
		}

		var providerSettings = await provider.ClearViewSettingsAsync(fields, cancellationToken).ConfigureAwait(false);
		if (providerSettings is null)
		{
			throw new InvalidOperationException("The view settings provider could not clear its persisted state.");
		}

		if ((providerSettings.Fields & fields) != ViewSettingsOverrideFields.None)
		{
			throw new InvalidOperationException("The view settings provider returned fields that were requested to be cleared.");
		}

		return providerSettings;
	}

	private async ValueTask<BrowseViewSettingsOverride> PatchApplicationViewSettingsAsync(
		BrowseLocation location,
		ViewSettingsScopeKey? scope,
		ViewSettingsOverrideFields fields,
		BrowseViewSettingsOverride replacement,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(location);

		ArgumentNullException.ThrowIfNull(replacement);

		if (scope is null)
		{
			var current = _unscopedSessionViewSettings.GetValueOrDefault(location) ?? new BrowseViewSettingsOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
			var updated = current.ReplaceFields(fields, replacement);
			if (updated.Fields == ViewSettingsOverrideFields.None)
			{
				_unscopedSessionViewSettings.Remove(location);
			}
			else
			{
				_unscopedSessionViewSettings[location] = updated;
			}

			return updated;
		}

		if (_viewSettingsStore is not null)
		{
			var updated = await _viewSettingsStore.PatchAsync(scope, fields, replacement, cancellationToken).ConfigureAwait(false);

			return updated ?? new BrowseViewSettingsOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
		}

		var sessionCurrent = _sessionViewSettings.GetValueOrDefault(scope) ?? new BrowseViewSettingsOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
		var sessionUpdated = sessionCurrent.ReplaceFields(fields, replacement);
		if (sessionUpdated.Fields == ViewSettingsOverrideFields.None)
		{
			_sessionViewSettings.Remove(scope);
		}
		else
		{
			_sessionViewSettings[scope] = sessionUpdated;
		}

		return sessionUpdated;
	}

	private async ValueTask<BrowseViewSettingsOverride> GetApplicationViewSettingsAsync(BrowseLocation location, ViewSettingsScopeKey? scope, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(location);

		if (scope is null)
		{
			return _unscopedSessionViewSettings.GetValueOrDefault(location) ?? new BrowseViewSettingsOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
		}

		if (_viewSettingsStore is not null)
		{
			return await _viewSettingsStore.GetAsync(scope, cancellationToken).ConfigureAwait(false) ?? new BrowseViewSettingsOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
		}

		return _sessionViewSettings.GetValueOrDefault(scope) ?? new BrowseViewSettingsOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
	}

	private static async ValueTask<BrowseViewSettingsOverride?> GetProviderViewSettingsAsync(IBrowseLocationContext context, CancellationToken cancellationToken)
	{
		if (context is not IViewSettingsPersistenceProvider provider)
		{
			return null;
		}

		try
		{
			return await provider.GetViewSettingsAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			CoreDiagnosticLog.Write("BrowseSession", $"Provider view settings read failed type={exception.GetType().Name} message={exception.Message}");

			return null;
		}
	}

	private static BrowseViewSettingsOverride RemoveOverrideFields(BrowseViewSettingsOverride current, ViewSettingsOverrideFields fields)
	{
		var retainedFields = current.Fields & ~fields;
		var retainedColumnMode = retainedFields.HasFlag(ViewSettingsOverrideFields.DetailsColumns) ? current.ColumnMode : ViewColumnSettingsMode.Replace;

		return new BrowseViewSettingsOverride(retainedFields, current.Values, retainedColumnMode);
	}

	private void RestorePendingPropertySort(long generation)
	{
		SchedulePropertySort(generation, requireCurrentGeneration: true);
	}

	private bool HasNavigationOperationsInFlight()
	{
		lock (_propertySortLock)
		{
			return _navigationOperationsInFlight is not 0;
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
				_unscopedSessionViewSettings.Clear();

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
		SchedulePropertySort(generation, requireCurrentGeneration: false);
	}

	private void SchedulePropertySort(long generation, bool requireCurrentGeneration)
	{
		CancellationTokenSource? previousCancellation;
		CancellationTokenSource cancellation;
		lock (_propertySortLock)
		{
			if (Volatile.Read(ref _isDisposed))
			{
				return;
			}

			if (_navigationOperationsInFlight is not 0)
			{
				_suppressedPropertySortGenerations.Add(generation);

				return;
			}

			if (requireCurrentGeneration && Generation != generation)
			{
				return;
			}

			previousCancellation = _propertySortCancellation;
			cancellation = new CancellationTokenSource();
			_propertySortCancellation = cancellation;
			_propertySortTask = ApplyPropertySortAsync(generation, cancellation);
			_propertySortGeneration = generation;
		}

		try
		{
			if (previousCancellation is not null)
			{
				CoreDiagnosticLog.Write("BrowseSession", "Previous property sort cancelled");
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
					_propertySortGeneration = 0;
				}
			}

			cancellation.Dispose();
		}
	}

	private Task? CancelPendingPropertySort(long? expectedGeneration = null)
	{
		CancellationTokenSource? cancellation;
		Task? task;
		lock (_propertySortLock)
		{
			if (expectedGeneration is not null && _propertySortGeneration != expectedGeneration.Value)
			{
				return null;
			}

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

	private sealed record BrowseNavigationSnapshot(
		BrowseLocation? Location,
		BrowseContextState? ActiveContext,
		BrowseItemProjection ItemProjection,
		ViewSettingsScopeKey? ViewSettingsScope,
		BrowseViewSettings ViewSettingsBaseline,
		BrowseViewSettingsOverride ProviderViewSettings,
		BrowseViewSettingsOverride ViewSettingsOverride,
		BrowseViewSettings ViewSettings,
		BrowseSelectionState Selection,
		IReadOnlyList<IStorableModel> Items,
		BrowsePresentationStore.Snapshot Presentations);

	private readonly record struct PreparedViewSettingsUpdate(bool SortChanged, IReadOnlyList<IStorableModel>? ExternalOrder);

	private sealed class PropertySortRestorer : IDisposable
	{
		private readonly BrowseSession _owner;
		private readonly long _generation;
		private bool _shouldRestore;

		public PropertySortRestorer(BrowseSession owner, long generation)
		{
			_owner = owner;
			_generation = generation;
		}

		public void MarkCanceled()
		{
			_shouldRestore = true;
		}

		public void MarkReplaced()
		{
			_shouldRestore = false;
		}

		public void Dispose()
		{
			if (_shouldRestore)
			{
				_owner.RestorePendingPropertySort(_generation);
			}
		}
	}

	private sealed class NavigationOperation : IDisposable
	{
		private readonly BrowseSession _owner;
		private readonly CancellationTokenSource _cancellation;
		private int _isDisposed;

		public CancellationToken Token => _cancellation.Token;
		public Task? PendingPropertySortTask { get; }

		public NavigationOperation(BrowseSession owner, CancellationTokenSource cancellation, Task? pendingPropertySortTask)
		{
			_owner = owner;
			_cancellation = cancellation;
			PendingPropertySortTask = pendingPropertySortTask;
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
		return BeginNavigation(cancellationToken, expectedGeneration: null, supersedeExisting: true)!;
	}

	private NavigationOperation? TryBeginNavigation(long expectedGeneration, CancellationToken cancellationToken)
	{
		return BeginNavigation(cancellationToken, expectedGeneration, supersedeExisting: false);
	}

	private NavigationOperation? BeginNavigation(CancellationToken cancellationToken, long? expectedGeneration, bool supersedeExisting)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _changeCoordinator.LifetimeToken);
		CancellationTokenSource? propertySortCancellation;
		Task? propertySortTask;
		CancellationTokenSource? previousCancellation;
		lock (_navigationCancellationLock)
		{
			if (!supersedeExisting && (_activeNavigationCancellation is not null || Generation != expectedGeneration))
			{
				operationCancellation.Dispose();

				return null;
			}

			previousCancellation = _activeNavigationCancellation;
			_activeNavigationCancellation = operationCancellation;
			lock (_propertySortLock)
			{
				if (Volatile.Read(ref _isDisposed))
				{
					_activeNavigationCancellation = previousCancellation;
					operationCancellation.Dispose();

					throw new ObjectDisposedException(nameof(BrowseSession));
				}

				_navigationOperationsInFlight++;
				propertySortCancellation = _propertySortCancellation;
				propertySortTask = _propertySortTask;
				if (propertySortTask is not null && _propertySortGeneration is not 0)
				{
					_suppressedPropertySortGenerations.Add(_propertySortGeneration);
				}
			}
		}

		var operation = new NavigationOperation(this, operationCancellation, propertySortTask);
		List<Exception>? cancellationExceptions = null;
		try
		{
			propertySortCancellation?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception exception)
		{
			(cancellationExceptions ??= []).Add(exception);
		}

		try
		{
			previousCancellation?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception exception)
		{
			(cancellationExceptions ??= []).Add(exception);
		}

		if (cancellationExceptions is not null)
		{
			operation.Dispose();

			throw new AggregateException("Navigation cancellation failed.", cancellationExceptions);
		}

		return operation;
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

		long[] suppressedPropertySortGenerations = [];
		lock (_propertySortLock)
		{
			_navigationOperationsInFlight--;
			if (_navigationOperationsInFlight is 0)
			{
				suppressedPropertySortGenerations = _suppressedPropertySortGenerations.ToArray();
				_suppressedPropertySortGenerations.Clear();
			}
		}

		operationCancellation.Dispose();
		foreach (var generation in suppressedPropertySortGenerations)
		{
			RestorePendingPropertySort(generation);
		}

		_changeCoordinator.Signal();
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
