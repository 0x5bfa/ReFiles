// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Sessions;
using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;
using Files.Core.Capabilities.Properties;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using Files.Core.Storage.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Files.Core;

/// <summary>
/// Configures storage sources, item capabilities, workspace services, and shell sessions.
/// </summary>
public sealed partial class FilesCoreBuilder : IAsyncDisposable
{
	private readonly List<IStorageSource> _sources = [];

	private readonly List<IStorageOperationHandler> _operationHandlers = [];
	private readonly List<Func<StorageWorkspace, IStorageOperationHandler>> _operationHandlerFactories = [];
	private IStorageOperationService? _storageOperations;

	private readonly List<Func<StorageWorkspace, IBrowseLocationHandler>> _locationHandlerFactories = [];

	private readonly List<IAsyncDisposable> _ownedServices = [];

	private readonly HashSet<string> _configuredModules = new(StringComparer.Ordinal);

	private readonly IViewSettingsStore _viewSettingsStore;

	private readonly IThumbnailCache _thumbnailCache;

	private readonly ILoggerFactory _loggerFactory;

	internal ILoggerFactory LoggerFactory => _loggerFactory;

	private readonly Lock _disposalLock = new();

	private Task? _disposeTask;

	private bool _isBuilt;

	private bool _isDisposed;

	/// <summary>Gets the item capability composition builder.</summary>
	public CapabilityBuilder Capabilities { get; }

	/// <summary>Initializes an empty Files.Core builder.</summary>
	/// <param name="viewSettingsStore">The optional view settings store.</param>
	/// <param name="thumbnailCache">The optional thumbnail cache.</param>
	/// <param name="loggerFactory">The optional borrowed logger factory.</param>
	public FilesCoreBuilder(IViewSettingsStore? viewSettingsStore = null, IThumbnailCache? thumbnailCache = null, ILoggerFactory? loggerFactory = null)
	{
		_viewSettingsStore = viewSettingsStore ?? new InMemoryViewSettingsStore();
		_thumbnailCache = thumbnailCache ?? new MemoryThumbnailCache();
		_loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

		Capabilities = new CapabilityBuilder().SetCombiner<IThumbnailSource>(new ThumbnailSourceCombiner()).SetCombiner<IPropertySource>(new PropertySourceCombiner())
			.SetCombiner<IPreviewSource>(new PreviewSourceCombiner()).AddWrapper<IThumbnailSource>(new ThumbnailCacheWrapper(_thumbnailCache));
	}

	/// <summary>Adds an owned storage source.</summary>
	/// <param name="source">The source to add.</param>
	/// <returns>This builder.</returns>
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

	/// <summary>Adds a storage operation handler.</summary>
	/// <param name="handler">The handler to add.</param>
	/// <returns>This builder.</returns>
	public FilesCoreBuilder AddStorageOperationHandler(IStorageOperationHandler handler)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(handler);

		EnsureLocalStorageOperations();
		_operationHandlers.Add(handler);

		return this;
	}

	/// <summary>Adds a browse location handler factory that depends only on the public workspace contract.</summary>
	/// <param name="handlerFactory">The handler factory.</param>
	/// <returns>This builder.</returns>
	public FilesCoreBuilder AddBrowseLocationHandler(Func<IStorageWorkspace, IBrowseLocationHandler> handlerFactory)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(handlerFactory);

		_locationHandlerFactories.Add(workspace => handlerFactory(workspace));

		return this;
	}

	/// <summary>Selects the operation boundary used by the runtime and all its browse models.</summary>
	/// <param name="operations">The borrowed operation service.</param>
	/// <returns>This builder.</returns>
	public FilesCoreBuilder UseStorageOperations(IStorageOperationService operations)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(operations);

		if (_operationHandlers.Count is not 0)
		{
			throw new InvalidOperationException("External execution cannot replace registered local handlers. Disable local operations when registering client-side storage sources.");
		}

		_storageOperations = operations;

		return this;
	}

	/// <summary>Builds the runtime and transfers ownership of configured resources.</summary>
	/// <returns>The composed runtime.</returns>
	public FilesCoreRuntime Build()
	{
		EnsureMutable();
		_isBuilt = true;

		StorageWorkspace? workspace = null;
		FilesApplicationSession? shellSession = null;
		try
		{
			var capabilityRegistry = Capabilities.Build();
			var modelFactory = new StorableModelFactory(capabilityRegistry);
			workspace = new StorageWorkspace(_sources, modelFactory);

			var handlers = new List<IBrowseLocationHandler>
			{
				new HomeBrowseLocationHandler(workspace),
				new FolderBrowseLocationHandler(workspace),
			};

			foreach (var factory in _locationHandlerFactories)
			{
				var handler = factory(workspace)
					?? throw new InvalidOperationException("A browse location handler factory returned null.");
				handlers.Add(handler);
			}

			var locationResolver = new BrowseLocationResolver(handlers);
			var storageOperations = _storageOperations ?? new StorageOperationService(_operationHandlers.Concat(_operationHandlerFactories.Select(factory => factory(workspace))));
			var search = new BrowseSearchService(handlers.OfType<IBrowseSearchProvider>());
			var services = new BrowseApplicationServices(workspace, storageOperations, search);
			var paneFactory = new BrowsePaneSessionFactory(locationResolver, _viewSettingsStore, _thumbnailCache, services: services, loggerFactory: _loggerFactory);
			shellSession = new FilesApplicationSession(paneFactory);
			IWindowsShellPreviewSessionFactory? previewSessions = null;
			if (_windowsShellPreviewSessionFactory is not null)
			{
				previewSessions = _windowsShellPreviewSessionFactory(workspace)
					?? throw new InvalidOperationException("The Windows Shell preview session factory returned null.");
			}

			var runtime = new FilesCoreRuntime(
				workspace,
				locationResolver,
				paneFactory,
				shellSession,
				storageOperations,
				_viewSettingsStore,
				_thumbnailCache,
				previewSessions,
				Array.AsReadOnly(_ownedServices.ToArray()));
			workspace = null;
			shellSession = null;

			return runtime;
		}
		catch (Exception buildError)
		{
			var cleanupErrors = new List<Exception>();
			if (shellSession is not null)
			{
				TryDisposeSynchronously(shellSession, cleanupErrors);
			}

			if (workspace is not null)
			{
				TryDisposeSynchronously(workspace, cleanupErrors);
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

	/// <summary>Disposes resources that have not been transferred to a runtime.</summary>
	/// <returns>A task that represents asynchronous disposal.</returns>
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

	private void EnsureLocalStorageOperations()
	{
		EnsureMutable();
		if (_storageOperations is not null)
		{
			throw new InvalidOperationException("Local operation handlers cannot be registered when an external execution service is configured.");
		}
	}

	private bool TryAddModule(string moduleId)
	{
		EnsureMutable();
		ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

		return _configuredModules.Add(moduleId);
	}

	private void AddStorageOperationHandlerFactory(Func<StorageWorkspace, IStorageOperationHandler> factory)
	{
		EnsureMutable();

		_operationHandlerFactories.Add(factory);
	}

	private FilesCoreBuilder AddStorageBrowseLocationHandler(Func<StorageWorkspace, IBrowseLocationHandler> handlerFactory)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(handlerFactory);

		_locationHandlerFactories.Add(handlerFactory);

		return this;
	}

	private void Own(IAsyncDisposable service)
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
