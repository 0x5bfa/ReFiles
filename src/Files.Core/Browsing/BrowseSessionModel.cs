// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Changes;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using System.Diagnostics;
using System.Threading.Channels;

namespace Files.Core.Browsing;

public sealed class BrowseSessionModel : IBrowseSessionModel, IBrowsePrefetchTarget
{
	private const int ChangeQueueCapacity = 256;

	private readonly IBrowseLocationResolver _locationResolver;
	private readonly IViewSettingsStore? _viewSettingsStore;
	private readonly IThumbnailCache? _thumbnailCache;
	private readonly Dictionary<BrowseLocation, BrowseViewSettings> _sessionViewSettings = [];
	private BrowseItemProjection _itemProjection;
	private readonly SemaphoreSlim _navigationLock = new(1, 1);
	private readonly SemaphoreSlim _refreshSignal = new(0, 1);
	private readonly Channel<QueuedFolderChange> _changeQueue = Channel.CreateBounded<QueuedFolderChange>(new BoundedChannelOptions(ChangeQueueCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false, });
	private readonly CancellationTokenSource _refreshLifetime = new();
	private readonly Lock _disposalLock = new();
	private readonly Lock _presentationLock = new();
	private readonly Lock _selectionLock = new();
	private readonly Dictionary<StorableKey, PresentationEntry> _presentations = [];
	private BrowseContextState? _activeContext;
	private BrowseContextState? _preparingContext;
	private Task? _disposeTask;
	private readonly Task _refreshPumpTask;
	private long _generationCounter;
	private long _requestedFullRefreshGeneration;
	private int _refreshSignalPending;
	private readonly Queue<QueuedFolderChange> _deferredChanges = [];
	private BrowseSelectionState _selection = BrowseSelectionState.Empty;
	private long _contentVersion;
	private long _itemsVersion;
	private bool _isDisposed;

	public BrowseSessionModel(IBrowseLocationResolver locationResolver, IViewSettingsStore? viewSettingsStore = null, IThumbnailCache? thumbnailCache = null)
	{
		ArgumentNullException.ThrowIfNull(locationResolver);

		_locationResolver = locationResolver;
		_viewSettingsStore = viewSettingsStore;
		_thumbnailCache = thumbnailCache;
		_itemProjection = new BrowseItemProjection(BrowseViewSettings.Default, GetSortPropertyValue);
		ViewSettings = BrowseViewSettings.Default;
		_refreshPumpTask = RefreshPumpAsync(_refreshLifetime.Token);
	}

	public BrowseLocation? Location { get; private set; }

	public IBrowseLocationContext? Context => Volatile.Read(ref _activeContext)?.Context;

	public long Generation => Volatile.Read(ref _activeContext)?.Generation ?? 0;

	public IReadOnlyList<IStorableModel> Items => Volatile.Read(ref _itemProjection).Items;

	public long ItemsVersion => Volatile.Read(ref _itemsVersion);

	long IBrowsePrefetchTarget.ContentVersion => Volatile.Read(ref _contentVersion);

	public BrowseSelectionState Selection => Volatile.Read(ref _selection);

	public BrowseViewSettings ViewSettings { get; private set; }

	public bool IsLoading { get; private set; }

	public Exception? Error { get; private set; }

	public event EventHandler? StateChanged;

	public event EventHandler<BrowseItemsChangedEventArgs>? ItemsChanged;

	public event EventHandler<BrowseItemPresentationChangedEventArgs>? ItemPresentationChanged;

	public event EventHandler? SelectionChanged;

	public async ValueTask NavigateAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);
		ArgumentNullException.ThrowIfNull(location);

		await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			await NavigateCoreAsync(location, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	private async ValueTask NavigateCoreAsync(BrowseLocation location, CancellationToken cancellationToken)
	{
		IsLoading = true;
		Error = null;
		OnStateChanged();

		try
		{
			var nextItems = new List<IStorableModel>();
			IBrowseLocationContext? nextLocationContext = null;
			BrowseContextState? nextContext = null;
			var committed = false;

			try
			{
				nextLocationContext = await _locationResolver.OpenAsync(location, cancellationToken).ConfigureAwait(false);
				ArgumentNullException.ThrowIfNull(nextLocationContext);

				var changes = nextLocationContext.LocationModel?.Get<IFolderChangeSource>();
				var generation = Interlocked.Increment(ref _generationCounter);
				nextContext = new BrowseContextState(this, nextLocationContext, changes, generation);
				Volatile.Write(ref _preparingContext, nextContext);

				var nextViewSettings = _viewSettingsStore is null
					? _sessionViewSettings.GetValueOrDefault(location, BrowseViewSettings.Default)
					: await _viewSettingsStore.GetAsync(location, cancellationToken).ConfigureAwait(false)
						?? BrowseViewSettings.Default;

				await nextContext.StartAsync(cancellationToken).ConfigureAwait(false);

				await foreach (var item in nextLocationContext.GetItemsAsync(cancellationToken).ConfigureAwait(false))
				{
					nextItems.Add(item);
				}

				var nextProjection = new BrowseItemProjection(nextViewSettings, GetSortPropertyValue);
				var nextItemChanges = nextProjection.Reset(nextItems);
				var previousContext = Volatile.Read(ref _activeContext);
				var previousItems = Items;
				var nextSelection = Equals(Location, location)
					? NormalizeSelection(Selection, nextProjection.Items)
					: BrowseSelectionState.Empty;
				Location = location;
				Volatile.Write(ref _activeContext, nextContext);
				Volatile.Write(ref _preparingContext, null);
				Volatile.Write(ref _itemProjection, nextProjection);
				ViewSettings = nextViewSettings;
				Error = null;
				ClearPresentations();
				nextLocationContext = null;
				nextContext = null;
				committed = true;
				PublishItemsChanged(nextItemChanges);
				SetSelectionState(nextSelection);
				SignalRefreshPump();

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
			finally
			{
				if (!committed)
				{
					if (nextContext is not null)
					{
						Volatile.Write(ref _preparingContext, null);
						Interlocked.CompareExchange(ref _requestedFullRefreshGeneration, 0, nextContext.Generation);
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
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			Error = exception;
			throw;
		}
		finally
		{
			IsLoading = false;
			OnStateChanged();
		}
	}

	public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);

		return Location is null
			? ValueTask.CompletedTask
			: NavigateAsync(Location, cancellationToken);
	}

	private async Task RefreshPumpAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				await _refreshSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
				Interlocked.Exchange(ref _refreshSignalPending, 0);

				var currentContext = Volatile.Read(ref _activeContext);
				if (currentContext is null)
				{
					continue;
				}

				try
				{
					if (await ProcessRequestedFullRefreshAsync(currentContext, cancellationToken).ConfigureAwait(false))
					{
						continue;
					}

					await ProcessChangesAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch
				{
					// NavigateCoreAsync records the refresh failure in Error.
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private async ValueTask<bool> ProcessRequestedFullRefreshAsync(BrowseContextState currentContext, CancellationToken cancellationToken)
	{
		var generation = Volatile.Read(ref _requestedFullRefreshGeneration);
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
			Interlocked.CompareExchange(ref _requestedFullRefreshGeneration, 0, generation);

			return false;
		}

		if (Interlocked.CompareExchange(ref _requestedFullRefreshGeneration, 0, generation) != generation)
		{
			return false;
		}

		await RefreshCurrentAsync(generation, cancellationToken).ConfigureAwait(false);

		return true;
	}

	private async ValueTask ProcessChangesAsync(CancellationToken cancellationToken)
	{
		while (TryReadNextChange(out var pendingChange))
		{
			var currentContext = Volatile.Read(ref _activeContext);
			if (currentContext is null)
			{
				_deferredChanges.Enqueue(pendingChange);

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
					_deferredChanges.Enqueue(pendingChange);

					return;
				}

				continue;
			}

			if (Volatile.Read(ref _requestedFullRefreshGeneration) == currentContext.Generation)
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

	private bool TryReadNextChange(out QueuedFolderChange pendingChange)
	{
		if (_deferredChanges.Count is not 0)
		{
			pendingChange = _deferredChanges.Dequeue();

			return true;
		}

		return _changeQueue.Reader.TryRead(out pendingChange);
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

	private async ValueTask<IncrementalApplyResult> ApplyChangeAsync(QueuedFolderChange pendingChange, CancellationToken cancellationToken)
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
				RemovePresentation(key);
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
					RemovePresentation(previousKeyToReplace);
					if (previousKeyToReplace != currentKey)
					{
						RemovePresentation(currentKey);
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
					RemovePresentation(key);
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

		var pendingChange = new QueuedFolderChange(context.Generation, change);
		if (!_changeQueue.Writer.TryWrite(pendingChange))
		{
			return RequestFullRefresh(context.Generation);
		}

		SignalRefreshPump();

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

		while (true)
		{
			var requestedGeneration = Volatile.Read(ref _requestedFullRefreshGeneration);
			if (requestedGeneration >= generation)
			{
				break;
			}

			if (Interlocked.CompareExchange(ref _requestedFullRefreshGeneration, generation, requestedGeneration) == requestedGeneration)
			{
				break;
			}
		}

		SignalRefreshPump();

		return true;
	}

	private bool IsKnownContext(BrowseContextState context)
	{
		return ReferenceEquals(Volatile.Read(ref _activeContext), context) || ReferenceEquals(Volatile.Read(ref _preparingContext), context);
	}

	private void SignalRefreshPump()
	{
		if (Interlocked.Exchange(ref _refreshSignalPending, 1) is not 0)
		{
			return;
		}

		try
		{
			_refreshSignal.Release();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	public async ValueTask UpdateViewSettingsAsync(BrowseViewSettings settings, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);
		ArgumentNullException.ThrowIfNull(settings);

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
				clearedThumbnails = ClearThumbnailPresentations();
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

	public bool TryGetPresentation(StorableKey key, out BrowseItemPresentation presentation)
	{
		lock (_presentationLock)
		{
			if (_presentations.TryGetValue(key, out var entry))
			{
				presentation = entry.Presentation;

				return true;
			}
		}

		presentation = null!;

		return false;
	}

	async ValueTask<bool> IBrowsePrefetchTarget.PublishPropertiesAsync(long generation, long expectedContentVersion, IStorableModel item, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken)
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

			var presentation = UpdatePresentation(key, item, properties, thumbnail: null, updateProperties: true, updateThumbnail: false);
			presentationChanged = new BrowseItemPresentationChangedEventArgs(key, presentation);

			if (!string.IsNullOrWhiteSpace(ViewSettings.SortPropertyId) && properties.ContainsKey(ViewSettings.SortPropertyId))
			{
				var changes = Volatile.Read(ref _itemProjection).UpdateSort(ViewSettings);
				PublishItemsChanged(changes, contentChanged: false);
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

			var presentation = UpdatePresentation(key, item, properties: null, thumbnail: thumbnail, updateProperties: false, updateThumbnail: true);
			presentationChanged = new BrowseItemPresentationChangedEventArgs(key, presentation);
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

		return Volatile.Read(ref _itemProjection).TryGet(key, out var current, out _)
			&& ReferenceEquals(current, item);
	}

	private BrowseItemPresentation UpdatePresentation(StorableKey key, IStorableModel item, IReadOnlyDictionary<string, object?>? properties, ThumbnailResult? thumbnail, bool updateProperties, bool updateThumbnail)
	{
		lock (_presentationLock)
		{
			var current = _presentations.TryGetValue(key, out var entry) && ReferenceEquals(entry.Item, item) ? entry.Presentation : new BrowseItemPresentation();
			var nextProperties = current.Properties;
			if (updateProperties)
			{
				var mergedProperties = new Dictionary<string, object?>(current.Properties, StringComparer.Ordinal);
				foreach (var pair in properties!)
				{
					mergedProperties[pair.Key] = pair.Value;
				}

				nextProperties = mergedProperties;
			}

			var next = new BrowseItemPresentation(nextProperties, updateThumbnail ? thumbnail : current.Thumbnail);
			_presentations[key] = new PresentationEntry(item, next);

			return next;
		}
	}

	private object? GetSortPropertyValue(IStorableModel item, string propertyId)
	{
		lock (_presentationLock)
		{
			var key = item.Reference.GetKey();

			return _presentations.TryGetValue(key, out var entry) &&
				ReferenceEquals(entry.Item, item) &&
				entry.Presentation.Properties.TryGetValue(propertyId, out var value)
					? value
					: null;
		}
	}

	private void ClearPresentations()
	{
		lock (_presentationLock)
		{
			_presentations.Clear();
		}
	}

	private BrowseItemPresentationChangedEventArgs[] ClearThumbnailPresentations()
	{
		var changes = new List<BrowseItemPresentationChangedEventArgs>();
		lock (_presentationLock)
		{
			foreach (var pair in _presentations.ToArray())
			{
				if (pair.Value.Presentation.Thumbnail is null)
				{
					continue;
				}

				var presentation = new BrowseItemPresentation(pair.Value.Presentation.Properties);
				_presentations[pair.Key] = new PresentationEntry(pair.Value.Item, presentation);
				changes.Add(new BrowseItemPresentationChangedEventArgs(pair.Key, presentation));
			}
		}

		return changes.ToArray();
	}

	private void RemovePresentation(StorableKey key)
	{
		lock (_presentationLock)
		{
			_presentations.Remove(key);
		}
	}

	public void SetSelection(IEnumerable<StorableKey> selectedKeys, StorableKey? focusedKey, StorableKey? anchorKey)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed), this);
		ArgumentNullException.ThrowIfNull(selectedKeys);

		var requestedSelection = new BrowseSelectionState(Array.AsReadOnly(selectedKeys.ToArray()), focusedKey, anchorKey);
		while (true)
		{
			var version = ItemsVersion;
			var normalized = NormalizeSelection(requestedSelection, Items);
			lock (_selectionLock)
			{
				if (version != ItemsVersion)
				{
					continue;
				}

				if (!TrySetSelectionState(normalized))
				{
					return;
				}
			}

			RaiseEvent(SelectionChanged);

			return;
		}
	}

	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

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
		_refreshLifetime.Cancel();
		SignalRefreshPump();
		try
		{
			await _refreshPumpTask.ConfigureAwait(false);
			_changeQueue.Writer.TryComplete();
			await _navigationLock.WaitAsync().ConfigureAwait(false);

			try
			{
				var items = Items;
				var currentContext = Volatile.Read(ref _activeContext);
				Volatile.Write(ref _itemProjection, new BrowseItemProjection(ViewSettings, GetSortPropertyValue));
				lock (_selectionLock)
				{
					Volatile.Write(ref _selection, BrowseSelectionState.Empty);
				}

				ClearPresentations();
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
			_refreshSignal.Dispose();
			_refreshLifetime.Dispose();
			GC.SuppressFinalize(this);
		}
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
		RaiseEvent(ItemsChanged, new BrowseItemsChangedEventArgs(previousVersion, version, changeSet.Changes));
	}

	private void SetSelectionState(BrowseSelectionState nextSelection)
	{
		ArgumentNullException.ThrowIfNull(nextSelection);

		lock (_selectionLock)
		{
			if (!TrySetSelectionState(nextSelection))
			{
				return;
			}
		}

		RaiseEvent(SelectionChanged);
	}

	private bool TrySetSelectionState(BrowseSelectionState nextSelection)
	{
		var currentSelection = Volatile.Read(ref _selection);
		if (currentSelection.FocusedKey == nextSelection.FocusedKey && currentSelection.AnchorKey == nextSelection.AnchorKey && currentSelection.SelectedKeys.SequenceEqual(nextSelection.SelectedKeys))
		{
			return false;
		}

		Volatile.Write(ref _selection, nextSelection);

		return true;
	}

	private void RemoveSelectionKey(StorableKey key)
	{
		var changed = false;
		lock (_selectionLock)
		{
			var currentSelection = Volatile.Read(ref _selection);
			if (!currentSelection.SelectedKeys.Contains(key) && currentSelection.FocusedKey != key && currentSelection.AnchorKey != key)
			{
				return;
			}

			changed = TrySetSelectionState(new BrowseSelectionState(
				Array.AsReadOnly(currentSelection.SelectedKeys .Where(selectedKey => selectedKey != key) .ToArray()),
				currentSelection.FocusedKey == key ? null : currentSelection.FocusedKey,
				currentSelection.AnchorKey == key ? null : currentSelection.AnchorKey));
		}

		if (changed)
		{
			RaiseEvent(SelectionChanged);
		}
	}

	private void MigrateSelection(StorableKey previousKey, StorableKey currentKey)
	{
		if (previousKey == currentKey)
		{
			return;
		}

		var changed = false;
		lock (_selectionLock)
		{
			var currentSelection = Volatile.Read(ref _selection);
			changed = TrySetSelectionState(new BrowseSelectionState(
				Array.AsReadOnly(currentSelection.SelectedKeys .Select(selectedKey => selectedKey == previousKey ? currentKey : selectedKey) .Distinct() .ToArray()),
				currentSelection.FocusedKey == previousKey ? currentKey : currentSelection.FocusedKey,
				currentSelection.AnchorKey == previousKey ? currentKey : currentSelection.AnchorKey));
		}

		if (changed)
		{
			RaiseEvent(SelectionChanged);
		}
	}

	private static BrowseSelectionState NormalizeSelection(BrowseSelectionState state, IReadOnlyList<IStorableModel> items)
	{
		var existingKeys = items.Select(static item => item.Reference.GetKey()).ToHashSet();

		return new BrowseSelectionState(
			Array.AsReadOnly(state.SelectedKeys .Where(existingKeys.Contains) .Distinct() .ToArray()),
			state.FocusedKey is { } focusedKey && existingKeys.Contains(focusedKey) ? focusedKey : null,
			state.AnchorKey is { } anchorKey && existingKeys.Contains(anchorKey) ? anchorKey : null);
	}

	private readonly record struct QueuedFolderChange(long Generation, FolderChange Change);

	private sealed record PresentationEntry(IStorableModel Item, BrowseItemPresentation Presentation);

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
		private readonly BrowseSessionModel _owner;

		private readonly IFolderChangeSource? _changes;

		private int _handlersAttached;

		public IBrowseLocationContext Context { get; }

		public long Generation { get; }

		public BrowseContextState(BrowseSessionModel owner, IBrowseLocationContext context, IFolderChangeSource? changes, long generation)
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

	private void OnStateChanged() => RaiseEvent(StateChanged);

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
				Trace.TraceError("BrowseSessionModel event handler failed: {0}", exception);
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
				Trace.TraceError("BrowseSessionModel event handler failed: {0}", exception);
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
