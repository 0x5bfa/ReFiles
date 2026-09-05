// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Sessions;
using Files.Core.Browsing;
using Files.Core.Capabilities.Previews;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Data;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using Files.Core.Windows;

namespace Files.Core.Composition;

/// <summary>
/// Owns the UI-independent storage workspace, shell session, and shared services for one process.
/// </summary>
public sealed class FilesCoreRuntime : IAsyncDisposable
{
	private readonly IReadOnlyList<IAsyncDisposable> _ownedServices;

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

	/// <summary>
	/// Gets the view settings store shared by browse sessions.
	/// </summary>
	public IViewSettingsStore ViewSettingsStore { get; }

	/// <summary>
	/// Gets the thumbnail cache shared by item capabilities.
	/// </summary>
	public IThumbnailCache ThumbnailCache { get; }

	/// <summary>
	/// Gets the optional factory for Windows Shell preview sessions.
	/// </summary>
	public IWindowsShellPreviewSessionFactory? WindowsShellPreviewSessions { get; }

	internal FilesCoreRuntime(
		IStorageWorkspace workspace,
		IBrowseLocationResolver locationResolver,
		IBrowsePaneSessionFactory paneSessionFactory,
		FilesApplicationSession shellSession,
		IStorageOperationService storageOperations,
		IViewSettingsStore viewSettingsStore,
		IThumbnailCache thumbnailCache,
		IWindowsShellPreviewSessionFactory? windowsShellPreviewSessions,
		IReadOnlyList<IAsyncDisposable> ownedServices)
	{
		Workspace = workspace;
		LocationResolver = locationResolver;
		PaneSessionFactory = paneSessionFactory;
		ShellSession = shellSession;
		StorageOperations = storageOperations;
		ViewSettingsStore = viewSettingsStore;
		ThumbnailCache = thumbnailCache;
		WindowsShellPreviewSessions = windowsShellPreviewSessions;
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
