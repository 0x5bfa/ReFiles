// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Sessions;
using Files.Core.Browsing;
using Files.Core.Capabilities.Previews;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Data;
using Files.Core.Storage;
using Files.Core.ViewSettings;

namespace Files.Core;

/// <summary>
/// Owns the UI-independent storage workspace, shell session, and shared services for one process.
/// </summary>
public sealed class FilesCoreRuntime : IFilesCoreRuntime, IProviderRuntimeServices
{
	private readonly IReadOnlyList<IAsyncDisposable> _ownedServices;
	private readonly IStorageReader _reader;
	private readonly IReadOnlyDictionary<Type, object> _providerServices;

	private readonly Lock _disposalLock = new();

	private Task? _disposeTask;

	/// <summary>
	/// Gets the storage workspace used by UI, CLI, and background hosts.
	/// </summary>
	public IStorageWorkspace Workspace { get; }

	/// <summary>
	/// Gets the root of the window, tab, and pane shell session graph.
	/// </summary>
	public FilesApplicationSession ShellSession { get; }

	/// <summary>
	/// Gets the resolver for typed browse locations.
	/// </summary>
	public IBrowseLocationResolver LocationResolver { get; }

	/// <summary>
	/// Gets the factory used to create browse pane sessions.
	/// </summary>
	public IBrowsePaneSessionFactory PaneSessionFactory { get; }

	/// <summary>
	/// Gets the UI-independent storage operation service.
	/// </summary>
	public IStorageOperationService StorageOperations { get; }

	/// <summary>Gets the optional monitor for accepted operations.</summary>
	public IStorageOperationMonitor? OperationMonitor => StorageOperations.Monitor;

	/// <summary>
	/// Gets the view settings store shared by browse sessions.
	/// </summary>
	public IViewSettingsStore ViewSettingsStore { get; }

	/// <summary>
	/// Gets the thumbnail cache shared by item capabilities.
	/// </summary>
	public IThumbnailCache ThumbnailCache { get; }

	IStorageReader IFilesCoreRuntime.Workspace => _reader;

	IFilesApplicationSession IFilesCoreRuntime.ShellSession => ShellSession;

	internal FilesCoreRuntime(
		IStorageWorkspace workspace,
		IBrowseLocationResolver locationResolver,
		IBrowsePaneSessionFactory paneSessionFactory,
		FilesApplicationSession shellSession,
		IStorageOperationService storageOperations,
		IViewSettingsStore viewSettingsStore,
		IThumbnailCache thumbnailCache,
		IReadOnlyDictionary<Type, object> providerServices,
		IReadOnlyList<IAsyncDisposable> ownedServices)
	{
		Workspace = workspace;
		_reader = new StorageReader(workspace);
		LocationResolver = locationResolver;
		PaneSessionFactory = paneSessionFactory;
		ShellSession = shellSession;
		StorageOperations = storageOperations;
		ViewSettingsStore = viewSettingsStore;
		ThumbnailCache = thumbnailCache;
		_providerServices = providerServices;
		_ownedServices = ownedServices;
	}

	/// <summary>
	/// Asynchronously disposes the shell session, shared services, and storage workspace.
	/// </summary>
	/// <returns>A task that represents the asynchronous disposal operation.</returns>
	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			_disposeTask ??= DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	/// <summary>Creates an independently owned browse pane using this runtime.</summary>
	/// <returns>The new pane.</returns>
	public IPaneSession CreateBrowsePane()
	{
		return PaneSessionFactory.Create();
	}

	/// <summary>Creates an independently owned settings pane.</summary>
	/// <returns>The new pane.</returns>
	public IPaneSession CreateSettingsPane()
	{
		return new PaneSession(new SettingsPaneSession());
	}

	TService? IProviderRuntimeServices.GetProviderService<TService>() where TService : class
	{
		return _providerServices.TryGetValue(typeof(TService), out var service) ? (TService)service : null;
	}

	private async Task DisposeCoreAsync()
	{
		var errors = new List<Exception>();

		await TryDisposeAsync(ShellSession, errors).ConfigureAwait(false);

		foreach (var service in _ownedServices.Reverse())
		{
			await TryDisposeAsync(service, errors).ConfigureAwait(false);
		}

		await TryDisposeAsync(Workspace, errors).ConfigureAwait(false);
		GC.SuppressFinalize(this);

		if (errors.Count is 1)
		{
			throw errors[0];
		}

		if (errors.Count > 1)
		{
			throw new AggregateException("One or more Files.Core runtime resources could not be disposed.", errors);
		}
	}

	private static async ValueTask TryDisposeAsync(IAsyncDisposable disposable, ICollection<Exception> errors)
	{
		try
		{
			await disposable.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
	}
}
