// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

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
	private const string ItemNamePropertyId = "System.ItemNameDisplay";

	private readonly IBrowseSessionModel _session;
	private readonly IBrowsePrefetchTarget? _target;
	private readonly Lock _syncRoot = new();
	private readonly int _thumbnailSize;
	private readonly HashSet<PrefetchWork> _activeWork = [];
	private PrefetchWork? _currentWork;
	private long _workIdCounter;
	private bool _isDisposed;

	public BrowsePrefetchCoordinator(IBrowseSessionModel session, int thumbnailSize = DefaultThumbnailSize)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thumbnailSize);

		_session = session;
		_target = session as IBrowsePrefetchTarget;
		_thumbnailSize = thumbnailSize;
		session.ItemsChanged += OnSessionItemsChanged;
	}

	public void UpdateViewport(BrowseViewport viewport, BrowseViewSettings settings, long browseGeneration)
	{
		ArgumentNullException.ThrowIfNull(viewport);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentOutOfRangeException.ThrowIfNegative(browseGeneration);

		CancellationTokenSource cancellation;
		PrefetchWork nextWork;
		PrefetchWork? previousWork;

		lock (_syncRoot)
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);

			cancellation = new CancellationTokenSource();
			var workId = checked(++_workIdCounter);
			var contentVersion = GetContentVersion();
			var task = Task.Run(() => PrefetchAsync(viewport, settings, workId, browseGeneration, contentVersion, cancellation.Token), CancellationToken.None);
			nextWork = new PrefetchWork(workId, browseGeneration, contentVersion, cancellation, task);
			previousWork = _currentWork;
			_currentWork = nextWork;
			_activeWork.Add(nextWork);
			_ = task.ContinueWith(_ => RemoveCompletedWork(nextWork), CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default);
		}

		previousWork?.Cancel();
	}

	public async ValueTask DisposeAsync()
	{
		PrefetchWork[] work;
		lock (_syncRoot)
		{
			if (_isDisposed)
			{
				return;
			}

			_isDisposed = true;
			_currentWork = null;
			work = [.. _activeWork];
		}

		_session.ItemsChanged -= OnSessionItemsChanged;
		foreach (var item in work)
		{
			item.Cancel();
		}

		if (work.Length is not 0)
		{
			await Task.WhenAll(work.Select(static item => item.Task)).ConfigureAwait(false);
		}

		foreach (var item in work)
		{
			item.Dispose();
		}
	}

	private async Task PrefetchAsync(BrowseViewport viewport, BrowseViewSettings settings, long workId, long generation, long contentVersion, CancellationToken cancellationToken)
	{
		try
		{
			var propertyIds = GetPropertyIds(settings);
			var requestedThumbnailSize = settings.LayoutMode is ViewLayoutMode.Details
				? DetailsThumbnailSize
				: _thumbnailSize;
			var items = _session.Items;
			foreach (var index in EnumerateIndices(items.Count, viewport))
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (!IsCurrent(workId, generation, contentVersion, cancellationToken))
				{
					return;
				}

				await PrefetchItemAsync(items[index], propertyIds, requestedThumbnailSize, viewport.Dpi, workId, generation, contentVersion, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch
		{
			// Prefetch is best effort; the foreground consumer can retry.
		}
	}

	private async ValueTask PrefetchItemAsync(IStorableModel item, IReadOnlyList<string> propertyIds, int requestedThumbnailSize, int dpi, long workId, long generation, long contentVersion, CancellationToken cancellationToken)
	{
		if (propertyIds.Count is not 0 && item.Get<IPropertySource>() is { } propertySource)
		{
			try
			{
				var properties = await propertySource.GetPropertiesAsync(new PropertyRequest(propertyIds), cancellationToken).ConfigureAwait(false);
				if (!IsCurrent(workId, generation, contentVersion, cancellationToken))
				{
					return;
				}

				if (_target is not null && !await _target.PublishPropertiesAsync(generation, contentVersion, item, properties, cancellationToken).ConfigureAwait(false))
				{
					return;
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

		if (!IsCurrent(workId, generation, contentVersion, cancellationToken))
		{
			return;
		}

		if (item.Get<IThumbnailSource>() is { } thumbnailSource)
		{
			try
			{
				var thumbnail = await thumbnailSource.GetThumbnailAsync(new ThumbnailRequest(requestedThumbnailSize, ThumbnailMode.PreferContent, dpi), cancellationToken).ConfigureAwait(false);
				if (thumbnail is null || !IsCurrent(workId, generation, contentVersion, cancellationToken))
				{
					return;
				}

				if (_target is not null)
				{
					await _target.PublishThumbnailAsync(generation, contentVersion, item, thumbnail, cancellationToken).ConfigureAwait(false);
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

	private bool IsCurrent(long workId, long generation, long contentVersion, CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested || _session.Generation != generation || GetContentVersion() != contentVersion)
		{
			return false;
		}

		lock (_syncRoot)
		{
			return !_isDisposed &&
				_currentWork is { Id: var currentId, Generation: var currentGeneration, ContentVersion: var currentContentVersion, } &&
				currentId == workId &&
				currentGeneration == generation &&
				currentContentVersion == contentVersion;
		}
	}

	private void OnSessionItemsChanged(object? sender, BrowseItemsChangedEventArgs args)
	{
		PrefetchWork? work = null;
		lock (_syncRoot)
		{
			if (_currentWork is { } current && (current.Generation != _session.Generation || current.ContentVersion != GetContentVersion()))
			{
				work = current;
				_currentWork = null;
			}
		}

		work?.Cancel();
	}

	private long GetContentVersion()
	{
		return _target?.ContentVersion ?? _session.ItemsVersion;
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

		return Array.AsReadOnly(propertyIds.ToArray());
	}

	private static bool IsModelProperty(string propertyId)
	{
		return propertyId.Equals("name", StringComparison.OrdinalIgnoreCase) ||
			propertyId.Equals(ItemNamePropertyId, StringComparison.Ordinal);
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

	private void RemoveCompletedWork(PrefetchWork work)
	{
		lock (_syncRoot)
		{
			_activeWork.Remove(work);
			if (ReferenceEquals(_currentWork, work))
			{
				_currentWork = null;
			}
		}

		work.Dispose();
	}

	private sealed class PrefetchWork : IDisposable
	{
		private readonly CancellationTokenSource _cancellation;

		private int _isDisposed;

		public long Id { get; }

		public long Generation { get; }

		public long ContentVersion { get; }

		public Task Task { get; }

		public PrefetchWork(long id, long generation, long contentVersion, CancellationTokenSource cancellation, Task task)
		{
			Id = id;
			Generation = generation;
			ContentVersion = contentVersion;
			_cancellation = cancellation;
			Task = task;
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
			if (Interlocked.Exchange(ref _isDisposed, 1) is 0)
			{
				_cancellation.Dispose();
			}
		}
	}
}
