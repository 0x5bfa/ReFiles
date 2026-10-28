// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.Core.Models;

/// <summary>Owns one independent archive view and exposes ordinary file and folder application models.</summary>
public sealed class ArchiveModel : IAsyncDisposable
{
	private readonly ArchiveStorageSource _source;
	private readonly IStorableModelFactory _models;
	private readonly Lock _disposalLock = new();
	private Task? _disposeTask;
	private bool _isDisposed;

	/// <summary>Gets the reopenable locator. Reopening reads current content rather than retaining this session.</summary>
	public ArchiveLocator Locator => _source.Locator;

	/// <summary>Gets the container reference observed while opening this view.</summary>
	public StorableReference Container => _source.Archive;

	/// <summary>Gets the borrowed root folder, valid until this model is disposed.</summary>
	public IFolderModel Root { get; }

	/// <summary>Gets every physical record, including duplicates and paths that cannot be projected into folders.</summary>
	public IReadOnlyList<ArchiveEntryInfo> Entries => _source.Catalog.Entries;

	internal IStorageSource RootSource => _source.RootSource;

	private ArchiveModel(ArchiveStorageSource source, IStorableModelFactory models)
	{
		_source = source;
		_models = models;
		Root = (IFolderModel)models.Create(source, source.GetFolder(string.Empty));
	}

	/// <summary>Opens an owned folder model inside this view.</summary>
	/// <param name="path">The normalized archive folder path.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>A folder owned by the caller and borrowing this archive's lifetime.</returns>
	public ValueTask<IFolderModel> OpenFolderAsync(string path, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult((IFolderModel)_models.Create(_source, _source.GetFolder(path)));
	}

	/// <summary>Resolves an exact item reference from this view.</summary>
	/// <param name="reference">The reference, scoped to this open session.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The owned item model, borrowing this archive's lifetime.</returns>
	public async ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return _models.Create(_source, await _source.ResolveAsync(reference, cancellationToken).ConfigureAwait(false));
	}

	/// <summary>Disposes this view, its reader, and its input without disposing configured storage sources.</summary>
	/// <returns>The asynchronous disposal operation.</returns>
	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			_isDisposed = true;
			_disposeTask ??= DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	/// <summary>Opens a container chain independently of browsing, using dependencies supplied by its execution owner.</summary>
	/// <param name="rootSource">The borrowed configured source containing the outermost archive.</param>
	/// <param name="locator">The current-content container chain to reopen.</param>
	/// <param name="models">The configured item model factory.</param>
	/// <param name="readers">The configured archive readers.</param>
	/// <param name="credentials">The optional credential resolver owned by the caller's execution scope.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>An independently owned archive model. Existing views are unaffected.</returns>
	public static async ValueTask<ArchiveModel> OpenAsync(IStorageSource rootSource, ArchiveLocator locator, IStorableModelFactory models, ArchiveBackendSelector readers,
		IArchiveCredentialResolver? credentials = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(rootSource);

		ArgumentNullException.ThrowIfNull(locator);

		ArgumentNullException.ThrowIfNull(models);

		ArgumentNullException.ThrowIfNull(readers);

		if (rootSource.SourceId != locator.Root.SourceId)
		{
			throw new ArgumentException("The locator belongs to another configured source.", nameof(locator));
		}

		if (locator.Parent is not null)
		{
			await using var parent = await OpenAsync(rootSource, locator.Parent, models, readers, credentials, cancellationToken).ConfigureAwait(false);
			var record = parent.Entries.SingleOrDefault(entry => entry.Id == locator.EntryId && ArchiveEntryPath.TryNormalize(entry.Path, out var path) && path == locator.EntryPath)
				?? throw new FileNotFoundException("The nested archive record changed or no longer exists.");
			await using var item = await parent.ResolveAsync(new StorableReference(parent._source.SourceId, $"entry:{record.Id}"), cancellationToken).ConfigureAwait(false);

			return await OpenItemAsync(rootSource, locator, item, models, readers, credentials, cancellationToken).ConfigureAwait(false);
		}

		await using var container = models.Create(rootSource, await rootSource.ResolveAsync(locator.Root, cancellationToken).ConfigureAwait(false));

		return await OpenItemAsync(rootSource, locator, container, models, readers, credentials, cancellationToken).ConfigureAwait(false);
	}

	private static async ValueTask<ArchiveModel> OpenItemAsync(IStorageSource rootSource, ArchiveLocator locator, IStorableModel container, IStorableModelFactory models,
		ArchiveBackendSelector readers, IArchiveCredentialResolver? credentials, CancellationToken cancellationToken)
	{
		Stream? input = await ArchiveStreamResolver.OpenReadAsync(container, cancellationToken).ConfigureAwait(false);
		IArchiveReadSession? session = null;
		ArchiveStorageSource? source = null;
		try
		{
			ArchiveCredential? credential = null;
			var attempt = 0;
			while (true)
			{
				var request = new ArchiveOpenRequest(container.Reference, container.Name, input, credential, attempt);
				var result = await readers.TryOpenAsync(request, cancellationToken).ConfigureAwait(false);
				if (result is ArchiveOpenResult.Success success)
				{
					session = success.Session;
					cancellationToken.ThrowIfCancellationRequested();

					var backend = readers.Backends.Single(reader => reader.Id == session.BackendId);
					source = new ArchiveStorageSource(rootSource, locator, request, session, backend, credentials);
					session = null;
					input = null;
					var model = new ArchiveModel(source, models);
					source = null;

					return model;
				}

				if (result is ArchiveOpenResult.CredentialRequired required)
				{
					var challenge = required.Challenge;
					if (credentials is null || ++attempt > 5)
					{
						throw new ArchiveCredentialRequiredException(challenge);
					}

					credential = await credentials.ResolveAsync(challenge, cancellationToken).ConfigureAwait(false)
						?? throw new OperationCanceledException("Archive credential entry was canceled.", cancellationToken);
					continue;
				}

				if (result is ArchiveOpenResult.Failed failed)
				{
					throw new ArchiveOpenException($"Archive '{container.Name}' could not be opened.", failed.Error);
				}

				throw new UnsupportedArchiveException(container.Name);
			}
		}
		finally
		{
			try
			{
				if (source is not null)
				{
					await source.DisposeAsync().ConfigureAwait(false);
				}
			}
			finally
			{
				try
				{
					if (session is not null)
					{
						await session.DisposeAsync().ConfigureAwait(false);
					}
				}
				finally
				{
					if (input is not null)
					{
						await input.DisposeAsync().ConfigureAwait(false);
					}
				}
			}
		}
	}

	private async Task DisposeCoreAsync()
	{
		try
		{
			await Root.DisposeAsync().ConfigureAwait(false);
		}
		finally
		{
			await _source.DisposeAsync().ConfigureAwait(false);
		}
	}
}
