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

public sealed partial class BrowseSession
{
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

			await NavigateCoreAsync(currentContext.Context.Location, 0, Task.FromResult<BrowseViewSettings?>(PersistedViewSettings), cancellationToken).ConfigureAwait(false);
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

}
