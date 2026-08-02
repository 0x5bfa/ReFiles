// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.AppModels;
using Files.Core.Browsing;
using Files.Core.ItemFeatures.Previews;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Data;
using Files.Core.Storage;
using Files.Core.ViewSettings;

namespace Files.Core.Composition;

/// <summary>
/// Owns the complete UI-independent Files model graph for one process.
/// </summary>
public sealed class FilesCoreRuntime : IAsyncDisposable
{
	private readonly IReadOnlyList<IAsyncDisposable> ownedServices;
	private readonly object disposalLock = new();
	private Task? disposeTask;

	internal FilesCoreRuntime(
		IFilesDataRoot dataRoot,
		IBrowseLocationResolver locationResolver,
		IBrowsePaneFactory paneFactory,
		FilesApplicationModel application,
		IStorageOperationService storageOperations,
		IViewSettingsStore viewSettingsStore,
		IThumbnailCache thumbnailCache,
		IWindowsShellPreviewSessionFactory? windowsShellPreviewSessions,
		IReadOnlyList<IAsyncDisposable> ownedServices)
	{
		DataRoot = dataRoot;
		LocationResolver = locationResolver;
		PaneFactory = paneFactory;
		Application = application;
		StorageOperations = storageOperations;
		ViewSettingsStore = viewSettingsStore;
		ThumbnailCache = thumbnailCache;
		WindowsShellPreviewSessions = windowsShellPreviewSessions;
		this.ownedServices = ownedServices;
	}

	public IFilesDataRoot DataRoot { get; }

	public IBrowseLocationResolver LocationResolver { get; }

	public IBrowsePaneFactory PaneFactory { get; }

	public FilesApplicationModel Application { get; }

	public IStorageOperationService StorageOperations { get; }

	public IViewSettingsStore ViewSettingsStore { get; }

	public IThumbnailCache ThumbnailCache { get; }

	public IWindowsShellPreviewSessionFactory? WindowsShellPreviewSessions { get; }

	public ValueTask DisposeAsync()
	{
		lock (disposalLock)
		{
			disposeTask ??= DisposeCoreAsync();
			return new ValueTask(disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		var errors = new List<Exception>();

		await TryDisposeAsync(Application, errors).ConfigureAwait(false);

		foreach (var service in ownedServices.Reverse())
		{
			await TryDisposeAsync(service, errors).ConfigureAwait(false);
		}

		await TryDisposeAsync(DataRoot, errors).ConfigureAwait(false);
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
