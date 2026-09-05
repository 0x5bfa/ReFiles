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
using Files.Core.Windows;

namespace Files.Core.Composition;

/// <summary>
/// Configures storage sources, item capabilities, workspace services, and shell sessions.
/// </summary>
public sealed class FilesCoreBuilder : IAsyncDisposable
{
	private readonly List<IStorageSource> _sources = [];

	private readonly List<IStorageOperationHandler> _operationHandlers = [];

	private readonly List<Func<StorageWorkspace, IBrowseLocationHandler>> _locationHandlerFactories = [];

	private readonly List<IAsyncDisposable> _ownedServices = [];

	private readonly HashSet<string> _configuredModules = new(StringComparer.Ordinal);

	private readonly IViewSettingsStore _viewSettingsStore;

	private readonly IThumbnailCache _thumbnailCache;

	private readonly Lock _disposalLock = new();

	private Func<IStorageWorkspace, IWindowsShellPreviewSessionFactory?>? _windowsShellPreviewSessionFactory;

	private Task? _disposeTask;

	private bool _isBuilt;

	private bool _isDisposed;

	/// <summary>Gets the item capability composition builder.</summary>
	public CapabilityBuilder Capabilities { get; }

	/// <summary>Initializes an empty Files.Core builder.</summary>
	/// <param name="viewSettingsStore">The optional view settings store.</param>
	/// <param name="thumbnailCache">The optional thumbnail cache.</param>
	public FilesCoreBuilder(IViewSettingsStore? viewSettingsStore = null, IThumbnailCache? thumbnailCache = null)
	{
		_viewSettingsStore = viewSettingsStore ?? new InMemoryViewSettingsStore();
		_thumbnailCache = thumbnailCache ?? new MemoryThumbnailCache();

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
			var paneFactory = new BrowsePaneSessionFactory(locationResolver, _viewSettingsStore, _thumbnailCache);
			shellSession = new FilesApplicationSession(paneFactory);
			var storageOperations = new StorageOperationService(_operationHandlers);
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

	internal bool TryAddModule(string moduleId)
	{
		EnsureMutable();
		ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

		return _configuredModules.Add(moduleId);
	}

	internal FilesCoreBuilder AddStorageBrowseLocationHandler(Func<StorageWorkspace, IBrowseLocationHandler> handlerFactory)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(handlerFactory);

		_locationHandlerFactories.Add(handlerFactory);

		return this;
	}

	internal void SetWindowsShellPreviewSessionFactory(Func<IStorageWorkspace, IWindowsShellPreviewSessionFactory> factory)
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
