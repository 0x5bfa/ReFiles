// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Threading.Channels;
using Files.Core.Diagnostics;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Properties;
using Files.Core.ItemFeatures.Thumbnails;
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
	private CancellationTokenSource? _propertyCancellation;
	private CancellationTokenSource? _thumbnailCancellation;
	private BrowseViewport? _lastViewport;
	private BrowseViewSettings _lastSettings;
	private long _workIdCounter;
	private long _latestWorkId;
	private long _lastRequestedGeneration;
	private long _lastObservedItemsVersion;
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
			var workId = checked(++_workIdCounter);
			_latestWorkId = workId;
			request = new PrefetchRequest(workId, browseGeneration, viewport, settings, _session.Items);
			propertyCancellation = _propertyCancellation;
			thumbnailCancellation = _thumbnailCancellation;
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
			await Parallel.ForEachAsync(
				indices,
				new ParallelOptions
				{
					MaxDegreeOfParallelism = MaxConcurrentPrefetchPerLane,
					CancellationToken = cancellation.Token,
				},
				(itemIndex, token) => PrefetchPropertiesAsync(request.Items[itemIndex], propertyIds, request, token)).ConfigureAwait(false);
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
			var thumbnailMode = request.Settings.LayoutMode is ViewLayoutMode.Details ? ThumbnailMode.Icon : ThumbnailMode.PreferContent;
			var indices = EnumerateIndices(request.Items.Count, request.Viewport).ToArray();
			await Parallel.ForEachAsync(
				indices,
				new ParallelOptions
				{
					MaxDegreeOfParallelism = MaxConcurrentPrefetchPerLane,
					CancellationToken = cancellation.Token,
				},
				(itemIndex, token) => PrefetchThumbnailAsync(request.Items[itemIndex], requestedThumbnailSize, thumbnailMode, request, token)).ConfigureAwait(false);
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

	private async ValueTask PrefetchPropertiesAsync(IStorableModel item, IReadOnlyList<string> propertyIds, PrefetchRequest request, CancellationToken cancellationToken)
	{
		if (!IsCurrent(request, cancellationToken) || item.Get<IPropertySource>() is not { } propertySource)
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

			var properties = await propertySource.GetPropertiesAsync(new PropertyRequest(propertyIds), cancellationToken).ConfigureAwait(false);
			if (IsCurrent(request, cancellationToken) && _target is not null)
			{
				await _target.PublishPropertiesAsync(request.Generation, item, properties, cancellationToken).ConfigureAwait(false);
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

	private async ValueTask PrefetchThumbnailAsync(IStorableModel item, int requestedThumbnailSize, ThumbnailMode thumbnailMode, PrefetchRequest request, CancellationToken cancellationToken)
	{
		if (!IsCurrent(request, cancellationToken) || item.Get<IThumbnailSource>() is not { } thumbnailSource)
		{
			return;
		}

		try
		{
			var thumbnailRequestCount = Interlocked.Increment(ref _diagnosticThumbnailRequestCount);
			if (thumbnailRequestCount is 1)
			{
				CoreDiagnosticLog.Write("BrowsePrefetchCoordinator", $"First thumbnail load started work={request.Id}");
			}

			var thumbnail = await thumbnailSource.GetThumbnailAsync(new ThumbnailRequest(requestedThumbnailSize, thumbnailMode, request.Viewport.Dpi), cancellationToken).ConfigureAwait(false);
			if (thumbnail is not null && IsCurrent(request, cancellationToken) && _target is not null)
			{
				await _target.PublishThumbnailAsync(request.Generation, item, thumbnail, cancellationToken).ConfigureAwait(false);
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
			}
			else
			{
				_thumbnailCancellation = cancellation;
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
			}
			else if (!propertyLane && ReferenceEquals(_thumbnailCancellation, cancellation))
			{
				_thumbnailCancellation = null;
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
			return !_isDisposed && request.Id == _latestWorkId;
		}
	}

	private void OnSessionStateChanged(object? sender, EventArgs args)
	{
		BrowseViewport viewport;
		BrowseViewSettings settings;
		long generation;
		lock (_syncRoot)
		{
			generation = _session.Generation;
			if (_isDisposed || _lastViewport is not { } currentViewport || (Equals(_lastSettings, _session.ViewSettings) && _lastRequestedGeneration == generation))
			{
				return;
			}

			viewport = currentViewport;
			settings = _session.ViewSettings;
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

			if (_propertyCancellation is not null || _thumbnailCancellation is not null)
			{
				_restartTimer.Change(ItemsChangedRestartDelay, Timeout.InfiniteTimeSpan);

				return;
			}

			viewport = currentViewport;
			settings = _session.ViewSettings;
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

	private sealed record PrefetchRequest(long Id, long Generation, BrowseViewport Viewport, BrowseViewSettings Settings, IReadOnlyList<IStorableModel> Items);
}
