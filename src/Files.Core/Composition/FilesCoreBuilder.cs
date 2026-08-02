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
	private readonly List<IStorageSource> sources = [];
	private readonly List<IStorageOperationHandler> operationHandlers = [];
	private readonly List<Func<IFilesDataRoot, IBrowseLocationHandler>> locationHandlerFactories = [];
	private readonly List<IAsyncDisposable> ownedServices = [];
	private readonly HashSet<string> configuredModules =
		new(StringComparer.Ordinal);
	private readonly IViewSettingsStore viewSettingsStore;
	private readonly IThumbnailCache thumbnailCache;
	private readonly object disposalLock = new();
	private Func<IFilesDataRoot, IWindowsShellPreviewSessionFactory?>?
		windowsShellPreviewSessionFactory;
	private Task? disposeTask;
	private bool isBuilt;
	private bool isDisposed;

	public FilesCoreBuilder(IViewSettingsStore? viewSettingsStore = null, IThumbnailCache? thumbnailCache = null)
	{
		this.viewSettingsStore =
			viewSettingsStore ?? new InMemoryViewSettingsStore();
		this.thumbnailCache =
			thumbnailCache ?? new MemoryThumbnailCache();

		ItemFeatures = new ItemFeatureBuilder()
			.SetCombiner<IThumbnailSource>(new ThumbnailSourceCombiner())
			.SetCombiner<IPropertySource>(new PropertySourceCombiner())
			.SetCombiner<IPreviewSource>(new PreviewSourceCombiner())
			.AddWrapper<IThumbnailSource>(new ThumbnailCacheWrapper(this.thumbnailCache));
	}

	public ItemFeatureBuilder ItemFeatures { get; }

	public FilesCoreBuilder AddStorageSource(IStorageSource source)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(source);

		if (sources.Any(candidate => candidate.SourceId == source.SourceId))
		{
			throw new InvalidOperationException($"Storage source '{source.SourceId}' is already registered.");
		}

		sources.Add(source);
		return this;
	}

	public FilesCoreBuilder AddStorageOperationHandler(IStorageOperationHandler handler)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(handler);
		operationHandlers.Add(handler);
		return this;
	}

	public FilesCoreBuilder AddBrowseLocationHandler(Func<IFilesDataRoot, IBrowseLocationHandler> handlerFactory)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(handlerFactory);
		locationHandlerFactories.Add(handlerFactory);
		return this;
	}

	public FilesCoreRuntime Build()
	{
		EnsureMutable();
		isBuilt = true;

		FilesDataRoot? dataRoot = null;
		FilesApplicationModel? application = null;
		try
		{
			var itemFeatureRegistry = ItemFeatures.Build();
			var modelFactory = new StorableModelFactory(itemFeatureRegistry);
			dataRoot = new FilesDataRoot(sources, modelFactory);

			var handlers = new List<IBrowseLocationHandler>
			{
				new HomeBrowseLocationHandler(dataRoot),
				new FolderBrowseLocationHandler(dataRoot),
			};
			foreach (var factory in locationHandlerFactories)
			{
				var handler = factory(dataRoot)
					?? throw new InvalidOperationException("A browse location handler factory returned null.");
				handlers.Add(handler);
			}

			var locationResolver = new BrowseLocationResolver(handlers);
			var paneFactory = new BrowsePaneFactory(locationResolver, viewSettingsStore, thumbnailCache);
			application = new FilesApplicationModel(paneFactory);
			var storageOperations =
				new StorageOperationService(operationHandlers);
			IWindowsShellPreviewSessionFactory? previewSessions = null;
			if (windowsShellPreviewSessionFactory is not null)
			{
				previewSessions =
					windowsShellPreviewSessionFactory(dataRoot)
					?? throw new InvalidOperationException("The Windows Shell preview session factory returned null.");
			}

			var runtime = new FilesCoreRuntime(
				dataRoot,
				locationResolver,
				paneFactory,
				application,
				storageOperations,
				viewSettingsStore,
				thumbnailCache,
				previewSessions,
				Array.AsReadOnly(ownedServices.ToArray()));
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
		lock (disposalLock)
		{
			if (disposeTask is not null)
			{
				return new ValueTask(disposeTask);
			}

			isDisposed = true;
			disposeTask = isBuilt
				? Task.CompletedTask
				: DisposeUnbuiltResourcesAsync();
			return new ValueTask(disposeTask);
		}
	}

	internal bool TryAddModule(string moduleId)
	{
		EnsureMutable();
		ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
		return configuredModules.Add(moduleId);
	}

	internal void SetWindowsShellPreviewSessionFactory(Func<IFilesDataRoot, IWindowsShellPreviewSessionFactory> factory)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(factory);

		if (windowsShellPreviewSessionFactory is not null)
		{
			throw new InvalidOperationException("A Windows Shell preview session factory is already configured.");
		}

		windowsShellPreviewSessionFactory = factory;
	}

	internal void Own(IAsyncDisposable service)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(service);

		if (!ownedServices.Any(existing => ReferenceEquals(existing, service)))
		{
			ownedServices.Add(service);
		}
	}

	private void DisposeSources(ICollection<Exception> errors)
	{
		foreach (var source in sources.AsEnumerable().Reverse())
		{
			TryDisposeSynchronously(source, errors);
		}
	}

	private void DisposeOwnedServices(ICollection<Exception> errors)
	{
		foreach (var service in ownedServices.AsEnumerable().Reverse())
		{
			TryDisposeSynchronously(service, errors);
		}
	}

	private static void TryDisposeSynchronously(IAsyncDisposable disposable, ICollection<Exception> errors)
	{
		try
		{
			disposable
				.DisposeAsync()
				.AsTask()
				.GetAwaiter()
				.GetResult();
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
	}

	private void EnsureMutable()
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
		if (isBuilt)
		{
			throw new InvalidOperationException("A FilesCoreBuilder can only build one runtime.");
		}
	}

	private async Task DisposeUnbuiltResourcesAsync()
	{
		var errors = new List<Exception>();

		foreach (var source in sources.AsEnumerable().Reverse())
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

		foreach (var service in ownedServices.AsEnumerable().Reverse())
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
