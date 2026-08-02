// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.AppModels;
using Files.Core.Browsing;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Previews;
using Files.Core.ItemFeatures.Properties;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;

namespace Files.Core.Composition;

/// <summary>
/// Configures storage sources, item features, and AppModel factories.
/// </summary>
public sealed class FilesCoreBuilder : IAsyncDisposable
{
	private readonly List<IStorageSource> _sources = [];

	private readonly List<IStorageOperationHandler> _operationHandlers = [];

	private readonly List<Func<IFilesDataRoot, IBrowseLocationHandler>> _locationHandlerFactories = [];

	private readonly List<IAsyncDisposable> _ownedServices = [];

	private readonly HashSet<string> _configuredModules = new(StringComparer.Ordinal);

	private readonly IViewSettingsStore _viewSettingsStore;

	private readonly IThumbnailCache _thumbnailCache;

	private readonly Lock _disposalLock = new();

	private Func<IFilesDataRoot, IWindowsShellPreviewSessionFactory?>? _windowsShellPreviewSessionFactory;

	private Task? _disposeTask;

	private bool _isBuilt;

	private bool _isDisposed;

	public ItemFeatureBuilder ItemFeatures { get; }

	public FilesCoreBuilder(IViewSettingsStore? viewSettingsStore = null, IThumbnailCache? thumbnailCache = null)
	{
		_viewSettingsStore = viewSettingsStore ?? new InMemoryViewSettingsStore();
		_thumbnailCache = thumbnailCache ?? new MemoryThumbnailCache();

		ItemFeatures = new ItemFeatureBuilder().SetCombiner<IThumbnailSource>(new ThumbnailSourceCombiner()).SetCombiner<IPropertySource>(new PropertySourceCombiner())
			.SetCombiner<IPreviewSource>(new PreviewSourceCombiner()).AddWrapper<IThumbnailSource>(new ThumbnailCacheWrapper(_thumbnailCache));
	}

	public FilesCoreBuilder AddStorageSource(IStorageSource source)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(source);

		if (_sources.Any(candidate => candidate.SourceId == source.SourceId))
		{
			throw new InvalidOperationException($"Storage source '{source.SourceId}' is already registered.");
		}

		_sources.Add(source);

		return this;
	}

	public FilesCoreBuilder AddStorageOperationHandler(IStorageOperationHandler handler)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(handler);

		_operationHandlers.Add(handler);

		return this;
	}

	public FilesCoreBuilder AddBrowseLocationHandler(Func<IFilesDataRoot, IBrowseLocationHandler> handlerFactory)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(handlerFactory);

		_locationHandlerFactories.Add(handlerFactory);

		return this;
	}

	public FilesCoreRuntime Build()
	{
		EnsureMutable();
		_isBuilt = true;

		FilesDataRoot? dataRoot = null;
		FilesApplicationModel? application = null;
		try
		{
			var itemFeatureRegistry = ItemFeatures.Build();
			var modelFactory = new StorableModelFactory(itemFeatureRegistry);
			dataRoot = new FilesDataRoot(_sources, modelFactory);

			var handlers = new List<IBrowseLocationHandler>
			{
				new HomeBrowseLocationHandler(dataRoot),
				new FolderBrowseLocationHandler(dataRoot),
			};

			foreach (var factory in _locationHandlerFactories)
			{
				var handler = factory(dataRoot)
					?? throw new InvalidOperationException("A browse location handler factory returned null.");
				handlers.Add(handler);
			}

			var locationResolver = new BrowseLocationResolver(handlers);
			var paneFactory = new BrowsePaneFactory(locationResolver, _viewSettingsStore, _thumbnailCache);
			application = new FilesApplicationModel(paneFactory);
			var storageOperations = new StorageOperationService(_operationHandlers);
			IWindowsShellPreviewSessionFactory? previewSessions = null;
			if (_windowsShellPreviewSessionFactory is not null)
			{
				previewSessions = _windowsShellPreviewSessionFactory(dataRoot)
					?? throw new InvalidOperationException("The Windows Shell preview session factory returned null.");
			}

			var runtime = new FilesCoreRuntime(dataRoot, locationResolver, paneFactory, application, storageOperations, _viewSettingsStore, _thumbnailCache, previewSessions, Array.AsReadOnly(_ownedServices.ToArray()));
			dataRoot = null;
			application = null;

			return runtime;
		}
		catch (Exception buildError)
		{
			var cleanupErrors = new List<Exception>();
			if (application is not null)
			{
				TryDisposeSynchronously(application, cleanupErrors);
			}

			if (dataRoot is not null)
			{
				TryDisposeSynchronously(dataRoot, cleanupErrors);
			}
			else
			{
				DisposeSources(cleanupErrors);
			}

			DisposeOwnedServices(cleanupErrors);
			if (cleanupErrors.Count is 0)
			{
				throw;
			}

			cleanupErrors.Insert(0, buildError);
			throw new AggregateException("Files.Core runtime construction and cleanup failed.", cleanupErrors);
		}
	}

	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			if (_disposeTask is not null)
			{
				return new ValueTask(_disposeTask);
			}

			_isDisposed = true;
			_disposeTask = _isBuilt ? Task.CompletedTask : DisposeUnbuiltResourcesAsync();

			return new ValueTask(_disposeTask);
		}
	}

	internal bool TryAddModule(string moduleId)
	{
		EnsureMutable();
		ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

		return _configuredModules.Add(moduleId);
	}

	internal void SetWindowsShellPreviewSessionFactory(Func<IFilesDataRoot, IWindowsShellPreviewSessionFactory> factory)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(factory);

		if (_windowsShellPreviewSessionFactory is not null)
		{
			throw new InvalidOperationException("A Windows Shell preview session factory is already configured.");
		}

		_windowsShellPreviewSessionFactory = factory;
	}

	internal void Own(IAsyncDisposable service)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(service);

		if (!_ownedServices.Any(existing => ReferenceEquals(existing, service)))
		{
			_ownedServices.Add(service);
		}
	}

	private void DisposeSources(ICollection<Exception> errors)
	{
		foreach (var source in _sources.AsEnumerable().Reverse())
		{
			TryDisposeSynchronously(source, errors);
		}
	}

	private void DisposeOwnedServices(ICollection<Exception> errors)
	{
		foreach (var service in _ownedServices.AsEnumerable().Reverse())
		{
			TryDisposeSynchronously(service, errors);
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

	private void EnsureMutable()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (_isBuilt)
		{
			throw new InvalidOperationException("A FilesCoreBuilder can only build one runtime.");
		}
	}

	private async Task DisposeUnbuiltResourcesAsync()
	{
		var errors = new List<Exception>();

		foreach (var source in _sources.AsEnumerable().Reverse())
		{
			try
			{
				await source.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
		}

		foreach (var service in _ownedServices.AsEnumerable().Reverse())
		{
			try
			{
				await service.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
		}

		GC.SuppressFinalize(this);

		if (errors.Count is 1)
		{
			throw errors[0];
		}

		if (errors.Count > 1)
		{
			throw new AggregateException("One or more unbuilt Files.Core resources could not be disposed.", errors);
		}
	}
}
