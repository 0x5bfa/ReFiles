// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.ViewSettings;

namespace Files.Core.Sessions;

/// <summary>Creates pane sessions containing browse content.</summary>
public interface IBrowsePaneSessionFactory
{
	/// <summary>Creates an owned pane session.</summary>
	/// <returns>The new pane session.</returns>
	PaneSession Create();
}

/// <summary>
/// Creates the fully owned model graph for one pane.
/// </summary>
public sealed class BrowsePaneSessionFactory : IBrowsePaneSessionFactory
{
	private readonly Func<IBrowseSession> _sessionFactory;
	private readonly Func<IBrowseSession, IBrowsePreviewModel> _previewFactory;
	private readonly int _historyCapacity;

	/// <summary>Initializes a factory from browse services.</summary>
	/// <param name="locationResolver">The resolver used to open browse locations.</param>
	/// <param name="viewSettingsStore">The optional view settings store.</param>
	/// <param name="thumbnailCache">The optional thumbnail cache.</param>
	/// <param name="historyCapacity">The maximum navigation history length.</param>
	public BrowsePaneSessionFactory(IBrowseLocationResolver locationResolver, IViewSettingsStore? viewSettingsStore = null, IThumbnailCache? thumbnailCache = null, int historyCapacity = 50)
		: this(
			() => new BrowseSession(locationResolver, viewSettingsStore, thumbnailCache),
			static session => new BrowsePreviewModel(session),
			historyCapacity)
	{
		ArgumentNullException.ThrowIfNull(locationResolver);

	}

	/// <summary>Initializes a factory from content collaborators.</summary>
	/// <param name="sessionFactory">The browse session factory.</param>
	/// <param name="previewFactory">The preview model factory.</param>
	/// <param name="historyCapacity">The maximum navigation history length.</param>
	public BrowsePaneSessionFactory(
		Func<IBrowseSession> sessionFactory,
		Func<IBrowseSession, IBrowsePreviewModel> previewFactory,
		int historyCapacity = 50)
	{
		ArgumentNullException.ThrowIfNull(sessionFactory);
		ArgumentNullException.ThrowIfNull(previewFactory);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(historyCapacity);

		_sessionFactory = sessionFactory;
		_previewFactory = previewFactory;
		_historyCapacity = historyCapacity;
	}

	/// <inheritdoc />
	public PaneSession Create()
	{
		var session = _sessionFactory() ?? throw new InvalidOperationException("The browse session factory returned null.");

		IBrowsePreviewModel? preview = null;
		BrowsePaneSession? content = null;

		try
		{
			preview = _previewFactory(session) ?? throw new InvalidOperationException("The browse preview factory returned null.");
			content = new BrowsePaneSession(session, preview, _historyCapacity);
			session = null!;
			preview = null;

			return new PaneSession(content);
		}
		catch (Exception creationError)
		{
			var cleanupErrors = new List<Exception>();
			if (content is not null)
			{
				TryDisposeSynchronously(content, cleanupErrors);
			}

			if (preview is not null)
			{
				TryDisposeSynchronously(preview, cleanupErrors);
			}

			if (session is not null)
			{
				TryDisposeSynchronously(session, cleanupErrors);
			}
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
