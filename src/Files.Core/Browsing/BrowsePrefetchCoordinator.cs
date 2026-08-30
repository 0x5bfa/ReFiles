// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Threading.Channels;
using Files.Core.Diagnostics;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Properties;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Models;
using Files.Core.ViewSettings;

namespace Files.Core.Browsing;

/// <summary>
/// Performs cancellable, snapshot-bound property and thumbnail prefetching.
/// </summary>
public sealed class BrowsePrefetchCoordinator : IBrowsePrefetchCoordinator
{
	private const int DefaultThumbnailSize = 96;
	private const int DetailsThumbnailSize = 16;
	private const int MaxConcurrentPrefetchPerLane = 2;
	private const int MaximumPropertyBatchSize = 32;
	private const string ItemNamePropertyId = "System.ItemNameDisplay";
	private static readonly TimeSpan ItemsChangedRestartDelay = TimeSpan.FromMilliseconds(100);

	private readonly IBrowseSession _session;
	private readonly IBrowsePrefetchTarget? _target;
	private readonly Lock _syncRoot = new();
	private readonly int _thumbnailSize;
	private readonly Channel<PrefetchRequest> _propertyRequests;
	private readonly Channel<PrefetchRequest> _thumbnailRequests;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Timer _restartTimer;
	private readonly Task _propertyWorkerTask;
	private readonly Task _thumbnailWorkerTask;
	private readonly Dictionary<IStorableModel, HashSet<string>> _prefetchedPropertyIds = new(ReferenceEqualityComparer.Instance);
	private CancellationTokenSource? _propertyCancellation;
	private CancellationTokenSource? _thumbnailCancellation;
	private PrefetchRequest? _activePropertyRequest;
	private PrefetchRequest? _activeThumbnailRequest;
	private PrefetchRequest? _latestRequest;
	private BrowseViewport? _lastViewport;
	private BrowseViewSettings _lastSettings;
	private BrowseViewSettings _lastObservedSessionSettings;
	private long _workIdCounter;
	private long _latestWorkId;
	private long _lastRequestedGeneration;
	private long _lastObservedItemsVersion;
	private long _prefetchedPropertiesGeneration;
	private int _diagnosticPropertyRequestCount;
	private int _diagnosticThumbnailRequestCount;
	private int _diagnosticViewportUpdateCount;
	private bool _isDisposed;

	/// <summary>Initializes a presenter-owned prefetch coordinator.</summary>
	/// <param name="session">The browse session that supplies item snapshots.</param>
	/// <param name="thumbnailSize">The requested thumbnail size.</param>
	public BrowsePrefetchCoordinator(IBrowseSession session, int thumbnailSize = DefaultThumbnailSize)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thumbnailSize);

		_session = session;
		_target = session as IBrowsePrefetchTarget;
		_thumbnailSize = thumbnailSize;
		_lastSettings = session.ViewSettings;
		_lastObservedSessionSettings = session.ViewSettings;
		_prefetchedPropertiesGeneration = session.Generation;
		_lastObservedItemsVersion = session.ItemsVersion;
		_propertyRequests = CreateRequestChannel();
		_thumbnailRequests = CreateRequestChannel();
		_restartTimer = new Timer(RestartPrefetch, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		_propertyWorkerTask = Task.Run(() => ProcessPropertyRequestsAsync(_lifetime.Token));
		_thumbnailWorkerTask = Task.Run(() => ProcessThumbnailRequestsAsync(_lifetime.Token));
		session.StateChanged += OnSessionStateChanged;
		session.ItemsChanged += OnSessionItemsChanged;
	}

	/// <inheritdoc />
	public void UpdateViewport(BrowseViewport viewport, BrowseViewSettings settings, long browseGeneration)
	{
		ArgumentNullException.ThrowIfNull(viewport);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentOutOfRangeException.ThrowIfNegative(browseGeneration);

		CancellationTokenSource? propertyCancellation;
		CancellationTokenSource? thumbnailCancellation;
		PrefetchRequest request;
		lock (_syncRoot)
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);

			_restartTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
			_lastViewport = viewport;
			_lastSettings = settings;
			_lastRequestedGeneration = browseGeneration;
			if (_prefetchedPropertiesGeneration != browseGeneration)
			{
				_prefetchedPropertyIds.Clear();
				_prefetchedPropertiesGeneration = browseGeneration;
			}

			var workId = checked(++_workIdCounter);
			_latestWorkId = workId;
			request = new PrefetchRequest(workId, browseGeneration, viewport, settings, _session.Items);
			_latestRequest = request;
			propertyCancellation = ShouldPreserveExpandingViewport(_activePropertyRequest, request) ? null : _propertyCancellation;
			thumbnailCancellation = ShouldPreserveExpandingViewport(_activeThumbnailRequest, request) ? null : _thumbnailCancellation;
		}

		Cancel(propertyCancellation);
		Cancel(thumbnailCancellation);
		_propertyRequests.Writer.TryWrite(request);
		_thumbnailRequests.Writer.TryWrite(request);
		var viewportUpdateCount = Interlocked.Increment(ref _diagnosticViewportUpdateCount);
		CoreDiagnosticLog.Write(
			"BrowsePrefetchCoordinator",
			$"Viewport queued count={viewportUpdateCount} work={request.Id} generation={browseGeneration} " +
			$"first={viewport.FirstVisibleIndex} visible={viewport.VisibleCount} lookAhead={viewport.LookAheadCount}");
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		CancellationTokenSource? propertyCancellation;
		CancellationTokenSource? thumbnailCancellation;
		lock (_syncRoot)
		{
			if (_isDisposed)
			{
				return;
			}

			_isDisposed = true;
			propertyCancellation = _propertyCancellation;
			thumbnailCancellation = _thumbnailCancellation;
			_propertyRequests.Writer.TryComplete();
			_thumbnailRequests.Writer.TryComplete();
		}

		_session.StateChanged -= OnSessionStateChanged;
		_session.ItemsChanged -= OnSessionItemsChanged;
		await _restartTimer.DisposeAsync().ConfigureAwait(false);
		_lifetime.Cancel();
		Cancel(propertyCancellation);
		Cancel(thumbnailCancellation);
		await Task.WhenAll(_propertyWorkerTask, _thumbnailWorkerTask).ConfigureAwait(false);
		_lifetime.Dispose();
		CoreDiagnosticLog.Write(
			"BrowsePrefetchCoordinator",
			$"disposed viewportUpdates={_diagnosticViewportUpdateCount} propertyRequests={_diagnosticPropertyRequestCount} " +
			$"thumbnailRequests={_diagnosticThumbnailRequestCount}");
	}

	private async Task ProcessPropertyRequestsAsync(CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var request in _propertyRequests.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
			{
				await ProcessPropertyRequestAsync(request, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private async Task ProcessThumbnailRequestsAsync(CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var request in _thumbnailRequests.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
			{
				await ProcessThumbnailRequestAsync(request, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private async Task ProcessPropertyRequestAsync(PrefetchRequest request, CancellationToken lifetimeToken)
	{
		using var cancellation = BeginLaneWork(request, propertyLane: true, lifetimeToken);
		if (cancellation is null)
		{
			return;
		}

		try
		{
			var propertyIds = GetPropertyIds(request.Settings);
			if (propertyIds.Count is 0)
			{
				return;
			}

			var indices = EnumerateIndices(request.Items.Count, request.Viewport).ToArray();
			var propertyRequest = new PropertyRequest(propertyIds, includeFormattedValues: true);
			var batchedSources = new Dictionary<IPropertyReader, List<(IStorableModel Item, ItemContext Context)>>(ReferenceEqualityComparer.Instance);
			var individualItems = new List<IStorableModel>();
			foreach (var itemIndex in indices)
			{
				var item = request.Items[itemIndex];
				if (!NeedsPropertyPrefetch(item, propertyIds, request.Generation))
				{
					continue;
				}

				if (item.Get<IPropertySource>() is not IBatchedPropertySource batchedSource)
				{
					individualItems.Add(item);

					continue;
				}

				if (!batchedSources.TryGetValue(batchedSource.Reader, out var items))
				{
					items = [];
					batchedSources.Add(batchedSource.Reader, items);
				}

				items.Add((item, batchedSource.Context));
			}

			foreach (var batch in batchedSources)
			{
				await PrefetchPropertyBatchAsync(batch.Key, batch.Value, propertyRequest, request, cancellation.Token).ConfigureAwait(false);
			}

			await Parallel.ForEachAsync(
				individualItems,
				new ParallelOptions
				{
					MaxDegreeOfParallelism = MaxConcurrentPrefetchPerLane,
					CancellationToken = cancellation.Token,
				},
				(item, token) => PrefetchPropertiesAsync(item, propertyRequest, request, token)).ConfigureAwait(false);
			CoreDiagnosticLog.Write("BrowsePrefetchCoordinator", $"Property viewport completed work={request.Id} items={indices.Length}");
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch
		{
			// Prefetch is best effort; the foreground consumer can retry.
		}
		finally
		{
			EndLaneWork(cancellation, propertyLane: true);
		}
	}

	private async Task ProcessThumbnailRequestAsync(PrefetchRequest request, CancellationToken lifetimeToken)
	{
		using var cancellation = BeginLaneWork(request, propertyLane: false, lifetimeToken);
		if (cancellation is null)
		{
			return;
		}

		try
		{
			var requestedThumbnailSize = request.Settings.LayoutMode is ViewLayoutMode.Details ? DetailsThumbnailSize : _thumbnailSize;
			var indices = EnumerateIndices(request.Items.Count, request.Viewport).ToArray();
			await ProcessThumbnailPassAsync(indices, requestedThumbnailSize, ThumbnailMode.Icon, request, cancellation.Token).ConfigureAwait(false);
			if (request.Settings.LayoutMode is not ViewLayoutMode.Details && IsCurrent(request, cancellation.Token))
			{
				await ProcessThumbnailPassAsync(indices, requestedThumbnailSize, ThumbnailMode.PreferContent, request, cancellation.Token).ConfigureAwait(false);
			}

			CoreDiagnosticLog.Write("BrowsePrefetchCoordinator", $"Thumbnail viewport completed work={request.Id} items={indices.Length}");
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch
		{
			// Prefetch is best effort; the foreground consumer can retry.
		}
		finally
		{
			EndLaneWork(cancellation, propertyLane: false);
		}
	}

	private async Task ProcessThumbnailPassAsync(int[] indices, int requestedThumbnailSize, ThumbnailMode thumbnailMode, PrefetchRequest request, CancellationToken cancellationToken)
	{
		var activeLoads = new List<PendingThumbnailLoad>(MaxConcurrentPrefetchPerLane);
		var completedLoads = new Dictionary<int, ThumbnailPrefetchResult?>();
		var nextLoadIndex = 0;
		var nextPublishIndex = 0;

		Task<ThumbnailPrefetchResult?> StartLoad(int sequence)
		{
			var item = request.Items[indices[sequence]];

			return LoadThumbnailAsync(item, requestedThumbnailSize, thumbnailMode, request, cancellationToken).AsTask();
		}

		while (nextLoadIndex < indices.Length && activeLoads.Count < MaxConcurrentPrefetchPerLane)
		{
			activeLoads.Add(new PendingThumbnailLoad(nextLoadIndex, StartLoad(nextLoadIndex)));
			nextLoadIndex++;
		}

		while (activeLoads.Count is not 0)
		{
			var completedTask = await Task.WhenAny(activeLoads.Select(static load => load.LoadTask)).ConfigureAwait(false);
			var completedLoadIndex = activeLoads.FindIndex(load => ReferenceEquals(load.LoadTask, completedTask));
			var completedLoad = activeLoads[completedLoadIndex];
			activeLoads.RemoveAt(completedLoadIndex);
			completedLoads.Add(completedLoad.Sequence, await completedTask.ConfigureAwait(false));

			if (nextLoadIndex < indices.Length && IsCurrent(request, cancellationToken))
			{
				activeLoads.Add(new PendingThumbnailLoad(nextLoadIndex, StartLoad(nextLoadIndex)));
				nextLoadIndex++;
			}

			while (completedLoads.Remove(nextPublishIndex, out var completedResult))
			{
				await PublishThumbnailAsync(completedResult, request, cancellationToken).ConfigureAwait(false);
				nextPublishIndex++;
			}
		}

		CoreDiagnosticLog.Write("BrowsePrefetchCoordinator", $"Thumbnail pass completed work={request.Id} mode={thumbnailMode} items={indices.Length}");
	}

	private async ValueTask PrefetchPropertyBatchAsync(
		IPropertyReader reader,
		IReadOnlyList<(IStorableModel Item, ItemContext Context)> items,
		PropertyRequest propertyRequest,
		PrefetchRequest request,
		CancellationToken cancellationToken)
	{
		if (!IsCurrent(request, cancellationToken))
		{
			return;
		}

		for (var offset = 0; offset < items.Count; offset += MaximumPropertyBatchSize)
		{
			if (!IsCurrent(request, cancellationToken))
			{
				return;
			}

			var batchSize = Math.Min(MaximumPropertyBatchSize, items.Count - offset);
			var batch = items.Skip(offset).Take(batchSize).ToArray();
			try
			{
				Interlocked.Increment(ref _diagnosticPropertyRequestCount);
				var contexts = batch.Select(static item => item.Context).ToArray();
				var propertiesByReference = await reader.GetPropertiesAsync(propertyRequest, contexts, cancellationToken).ConfigureAwait(false);
				foreach (var item in batch)
				{
					if (!IsCurrent(request, cancellationToken))
					{
						return;
					}

					if (propertiesByReference.TryGetValue(item.Context.Reference, out var properties) && _target is not null)
					{
						await _target.PublishPropertiesAsync(request.Generation, item.Item, properties, cancellationToken).ConfigureAwait(false);
						MarkPropertiesPrefetched(item.Item, propertyRequest.PropertyIds, request.Generation);
					}
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch
			{
				// Prefetch is best effort; the foreground consumer can retry.
			}
		}
	}

	private async ValueTask PrefetchPropertiesAsync(IStorableModel item, PropertyRequest propertyRequest, PrefetchRequest request, CancellationToken cancellationToken)
	{
		if (!IsCurrent(request, cancellationToken) || !NeedsPropertyPrefetch(item, propertyRequest.PropertyIds, request.Generation) || item.Get<IPropertySource>() is not { } propertySource)
		{
			return;
		}

		try
		{
			var propertyRequestCount = Interlocked.Increment(ref _diagnosticPropertyRequestCount);
			if (propertyRequestCount is 1)
			{
				CoreDiagnosticLog.Write("BrowsePrefetchCoordinator", $"First property load started work={request.Id}");
			}

			var properties = await propertySource.GetPropertiesAsync(propertyRequest, cancellationToken).ConfigureAwait(false);
			if (IsCurrent(request, cancellationToken) && _target is not null)
			{
				await _target.PublishPropertiesAsync(request.Generation, item, properties, cancellationToken).ConfigureAwait(false);
				MarkPropertiesPrefetched(item, propertyRequest.PropertyIds, request.Generation);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			// Prefetch is best effort; the foreground consumer can retry.
		}
	}

	private async ValueTask<ThumbnailPrefetchResult?> LoadThumbnailAsync(
		IStorableModel item, int requestedThumbnailSize, ThumbnailMode thumbnailMode, PrefetchRequest request, CancellationToken cancellationToken)
	{
		if (!IsCurrent(request, cancellationToken) || item.Get<IThumbnailSource>() is not { } thumbnailSource)
		{
			return null;
		}

		try
		{
			var thumbnailRequestCount = Interlocked.Increment(ref _diagnosticThumbnailRequestCount);
			if (thumbnailRequestCount is 1)
			{
				CoreDiagnosticLog.Write("BrowsePrefetchCoordinator", $"First thumbnail load started work={request.Id}");
			}

			var thumbnail = await thumbnailSource.GetThumbnailAsync(new ThumbnailRequest(requestedThumbnailSize, thumbnailMode, request.Viewport.Dpi), cancellationToken).ConfigureAwait(false);

			return thumbnail is null ? null : new ThumbnailPrefetchResult(item, thumbnail);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return null;
		}
		catch
		{
			// Prefetch is best effort; the foreground consumer can retry.

			return null;
		}
	}

	private async ValueTask PublishThumbnailAsync(ThumbnailPrefetchResult? result, PrefetchRequest request, CancellationToken cancellationToken)
	{
		if (result is null || !IsCurrent(request, cancellationToken) || _target is null)
		{
			return;
		}

		try
		{
			await _target.PublishThumbnailAsync(request.Generation, result.Item, result.Thumbnail, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch
		{
			// Prefetch is best effort; the foreground consumer can retry.
		}
	}

	private CancellationTokenSource? BeginLaneWork(PrefetchRequest request, bool propertyLane, CancellationToken lifetimeToken)
	{
		lock (_syncRoot)
		{
			if (_isDisposed || request.Id != _latestWorkId || request.Generation != _session.Generation)
			{
				return null;
			}

			var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
			if (propertyLane)
			{
				_propertyCancellation = cancellation;
				_activePropertyRequest = request;
			}
			else
			{
				_thumbnailCancellation = cancellation;
				_activeThumbnailRequest = request;
			}

			return cancellation;
		}
	}

	private void EndLaneWork(CancellationTokenSource cancellation, bool propertyLane)
	{
		lock (_syncRoot)
		{
			if (propertyLane && ReferenceEquals(_propertyCancellation, cancellation))
			{
				_propertyCancellation = null;
				_activePropertyRequest = null;
			}
			else if (!propertyLane && ReferenceEquals(_thumbnailCancellation, cancellation))
			{
				_thumbnailCancellation = null;
				_activeThumbnailRequest = null;
			}
		}
	}

	private bool IsCurrent(PrefetchRequest request, CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested || _session.Generation != request.Generation)
		{
			return false;
		}

		lock (_syncRoot)
		{
			return !_isDisposed;
		}
	}

	private bool NeedsPropertyPrefetch(IStorableModel item, IReadOnlyList<string> propertyIds, long generation)
	{
		lock (_syncRoot)
		{
			return _prefetchedPropertiesGeneration != generation
				|| !_prefetchedPropertyIds.TryGetValue(item, out var prefetchedIds)
				|| propertyIds.Any(propertyId => !prefetchedIds.Contains(propertyId));
		}
	}

	private void MarkPropertiesPrefetched(IStorableModel item, IReadOnlyList<string> propertyIds, long generation)
	{
		lock (_syncRoot)
		{
			if (_isDisposed || _prefetchedPropertiesGeneration != generation)
			{
				return;
			}

			if (!_prefetchedPropertyIds.TryGetValue(item, out var prefetchedIds))
			{
				prefetchedIds = new HashSet<string>(StringComparer.Ordinal);
				_prefetchedPropertyIds.Add(item, prefetchedIds);
			}

			prefetchedIds.UnionWith(propertyIds);
		}
	}

	private static bool ShouldPreserveExpandingViewport(PrefetchRequest? activeRequest, PrefetchRequest nextRequest)
	{
		if (activeRequest is null || activeRequest.Generation != nextRequest.Generation || !Equals(activeRequest.Settings, nextRequest.Settings))
		{
			return false;
		}

		var activeViewport = activeRequest.Viewport;
		var nextViewport = nextRequest.Viewport;

		return activeViewport.FirstVisibleIndex == nextViewport.FirstVisibleIndex
			&& activeViewport.LookAheadCount == nextViewport.LookAheadCount
			&& activeViewport.Dpi == nextViewport.Dpi
			&& nextViewport.VisibleCount > activeViewport.VisibleCount;
	}

	private void OnSessionStateChanged(object? sender, EventArgs args)
	{
		BrowseViewport viewport;
		BrowseViewSettings settings;
		long generation;
		lock (_syncRoot)
		{
			generation = _session.Generation;
			if (_isDisposed || _lastViewport is not { } currentViewport)
			{
				return;
			}

			var sessionSettings = _session.ViewSettings;
			var generationChanged = _lastRequestedGeneration != generation;
			var settingsChanged = !Equals(_lastObservedSessionSettings, sessionSettings);
			_lastObservedSessionSettings = sessionSettings;
			if (!generationChanged && (!settingsChanged || Equals(_lastSettings, sessionSettings)))
			{
				return;
			}

			viewport = currentViewport;
			settings = sessionSettings;
		}

		TryUpdateViewport(viewport, settings, generation);
	}

	private void OnSessionItemsChanged(object? sender, BrowseItemsChangedEventArgs args)
	{
		lock (_syncRoot)
		{
			if (_isDisposed || args.Version <= _lastObservedItemsVersion)
			{
				return;
			}

			_lastObservedItemsVersion = args.Version;
			if (_lastViewport is not null && _session.Generation is not 0)
			{
				_restartTimer.Change(ItemsChangedRestartDelay, Timeout.InfiniteTimeSpan);
			}
		}
	}

	private void RestartPrefetch(object? state)
	{
		BrowseViewport viewport;
		BrowseViewSettings settings;
		long generation;
		lock (_syncRoot)
		{
			if (_isDisposed || _lastViewport is not { } currentViewport || _session.Generation is 0)
			{
				return;
			}

			if ((_propertyCancellation is not null || _thumbnailCancellation is not null)
				&& _latestRequest is { } latestRequest
				&& ViewportItemsMatch(latestRequest, _session.Items))
			{
				_restartTimer.Change(ItemsChangedRestartDelay, Timeout.InfiniteTimeSpan);

				return;
			}

			viewport = currentViewport;
			settings = _lastSettings;
			generation = _session.Generation;
		}

		TryUpdateViewport(viewport, settings, generation);
	}

	private void TryUpdateViewport(BrowseViewport viewport, BrowseViewSettings settings, long generation)
	{
		try
		{
			UpdateViewport(viewport, settings, generation);
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private static Channel<PrefetchRequest> CreateRequestChannel()
	{
		return Channel.CreateBounded<PrefetchRequest>(new BoundedChannelOptions(1)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
			SingleWriter = false,
		});
	}

	private static void Cancel(CancellationTokenSource? cancellation)
	{
		try
		{
			cancellation?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private static IReadOnlyList<string> GetPropertyIds(BrowseViewSettings settings)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var propertyIds = new List<string>();
		foreach (var column in settings.Columns)
		{
			if (column.IsVisible && !string.IsNullOrWhiteSpace(column.PropertyId) && !IsModelProperty(column.PropertyId) && seen.Add(column.PropertyId))
			{
				propertyIds.Add(column.PropertyId);
			}
		}

		if (!string.IsNullOrWhiteSpace(settings.SortPropertyId) && !IsModelProperty(settings.SortPropertyId) && seen.Add(settings.SortPropertyId))
		{
			propertyIds.Add(settings.SortPropertyId);
		}

		if (!string.IsNullOrWhiteSpace(settings.GroupPropertyId) && !IsModelProperty(settings.GroupPropertyId) && seen.Add(settings.GroupPropertyId))
		{
			propertyIds.Add(settings.GroupPropertyId);
		}

		return Array.AsReadOnly(propertyIds.ToArray());
	}

	private static bool IsModelProperty(string propertyId)
	{
		return propertyId.Equals("name", StringComparison.OrdinalIgnoreCase) || propertyId.Equals(ItemNamePropertyId, StringComparison.Ordinal);
	}

	private static IEnumerable<int> EnumerateIndices(int itemCount, BrowseViewport viewport)
	{
		if (itemCount is 0 || viewport.VisibleCount is 0 || viewport.FirstVisibleIndex >= itemCount)
		{
			yield break;
		}

		var visibleStart = Math.Min(viewport.FirstVisibleIndex, itemCount);
		var visibleEnd = (int)Math.Min(itemCount, (long)visibleStart + viewport.VisibleCount);
		var lookBehindStart = (int)Math.Max(0L, (long)visibleStart - viewport.LookAheadCount);
		var lookAheadEnd = (int)Math.Min(itemCount, (long)visibleEnd + viewport.LookAheadCount);

		for (var index = visibleStart; index < visibleEnd; index++)
		{
			yield return index;
		}

		for (var index = visibleEnd; index < lookAheadEnd; index++)
		{
			yield return index;
		}

		for (var index = visibleStart - 1; index >= lookBehindStart; index--)
		{
			yield return index;
		}
	}

	private static bool ViewportItemsMatch(PrefetchRequest request, IReadOnlyList<IStorableModel> currentItems)
	{
		foreach (var index in EnumerateIndices(request.Items.Count, request.Viewport))
		{
			if (index >= currentItems.Count || !ReferenceEquals(request.Items[index], currentItems[index]))
			{
				return false;
			}
		}

		return true;
	}

	private sealed record PrefetchRequest(long Id, long Generation, BrowseViewport Viewport, BrowseViewSettings Settings, IReadOnlyList<IStorableModel> Items);

	private sealed record PendingThumbnailLoad(int Sequence, Task<ThumbnailPrefetchResult?> LoadTask);

	private sealed record ThumbnailPrefetchResult(IStorableModel Item, ThumbnailResult Thumbnail);
}
