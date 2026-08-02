// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.ViewSettings;

namespace Files.Core.AppModels;

public interface IBrowsePaneFactory
{
	PaneModel Create();
}

/// <summary>
/// Creates the fully owned model graph for one pane.
/// </summary>
public sealed class BrowsePaneFactory : IBrowsePaneFactory
{
	private readonly Func<IBrowseSessionModel> _sessionFactory;
	private readonly Func<IBrowseSessionModel, IBrowsePreviewModel> _previewFactory;
	private readonly Func<IBrowseSessionModel, IBrowsePrefetchCoordinator> _prefetchFactory;
	private readonly int _historyCapacity;

	public BrowsePaneFactory(IBrowseLocationResolver locationResolver, IViewSettingsStore? viewSettingsStore = null, IThumbnailCache? thumbnailCache = null, int thumbnailSize = 96, int historyCapacity = 50)
		: this(
			() => new BrowseSessionModel(locationResolver, viewSettingsStore, thumbnailCache),
			static session => new BrowsePreviewModel(session),
			session => new BrowsePrefetchCoordinator(session, thumbnailSize),
			historyCapacity)
	{
		ArgumentNullException.ThrowIfNull(locationResolver);

	}

	public BrowsePaneFactory(
		Func<IBrowseSessionModel> sessionFactory,
		Func<IBrowseSessionModel, IBrowsePreviewModel> previewFactory,
		Func<IBrowseSessionModel, IBrowsePrefetchCoordinator> prefetchFactory,
		int historyCapacity = 50)
	{
		ArgumentNullException.ThrowIfNull(sessionFactory);
		ArgumentNullException.ThrowIfNull(previewFactory);
		ArgumentNullException.ThrowIfNull(prefetchFactory);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(historyCapacity);

		_sessionFactory = sessionFactory;
		_previewFactory = previewFactory;
		_prefetchFactory = prefetchFactory;
		_historyCapacity = historyCapacity;
	}

	public PaneModel Create()
	{
		var session = _sessionFactory() ?? throw new InvalidOperationException("The browse session factory returned null.");

		IBrowsePreviewModel? preview = null;
		IBrowsePrefetchCoordinator? prefetch = null;

		try
		{
			preview = _previewFactory(session) ?? throw new InvalidOperationException("The browse preview factory returned null.");
			prefetch = _prefetchFactory(session) ?? throw new InvalidOperationException("The browse prefetch factory returned null.");

			return new PaneModel(session, preview, prefetch, _historyCapacity);
		}
		catch (Exception creationError)
		{
			var cleanupErrors = new List<Exception>();
			if (prefetch is not null)
			{
				TryDisposeSynchronously(prefetch, cleanupErrors);
			}

			if (preview is not null)
			{
				TryDisposeSynchronously(preview, cleanupErrors);
			}

			TryDisposeSynchronously(session, cleanupErrors);
			if (cleanupErrors.Count is 0)
			{
				throw;
			}

			cleanupErrors.Insert(0, creationError);

			throw new AggregateException("Pane construction and cleanup failed.", cleanupErrors);
		}
	}

	private static void TryDisposeSynchronously(IAsyncDisposable disposable, ICollection<Exception> errors)
	{
		try
		{
			disposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
	}
}
