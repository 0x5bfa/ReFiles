// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.Core.Browsing;

/// <summary>
/// Owns an archive mount for one active browse location.
/// </summary>
public sealed class ArchiveBrowseLocationContext : IBrowseLocationContext, IBrowseLocationItemResolver, IBrowseLocationParentResolver
{
	private readonly ArchiveLocation _location;

	private readonly IStorableModel _archiveModel;

	private readonly IFolderModel _folderModel;

	private readonly IArchiveMount _mount;

	private readonly IStorageWorkspace _workspace;

	private readonly IStorableModelFactory _modelFactory;

	private int _isDisposed;

	/// <inheritdoc />
	public BrowseLocation Location => _location;

	/// <inheritdoc />
	public IStorableModel LocationModel => _folderModel;

	/// <inheritdoc />
	public bool CanGetParent =>
		!string.IsNullOrEmpty(_location.EntryPath) ||
		_archiveModel.GetCoreModel() is OwlCore.Storage.IStorableChild;

	/// <summary>Initializes an archive browse context and takes ownership of the archive resources.</summary>
	/// <param name="location">The archive location.</param>
	/// <param name="archiveModel">The archive item model.</param>
	/// <param name="folderModel">The mounted folder model.</param>
	/// <param name="mount">The archive mount.</param>
	/// <param name="workspace">The storage workspace.</param>
	/// <param name="modelFactory">The internal model factory for mounted entries.</param>
	public ArchiveBrowseLocationContext(
		ArchiveLocation location,
		IStorableModel archiveModel,
		IFolderModel folderModel,
		IArchiveMount mount,
		IStorageWorkspace workspace,
		IStorableModelFactory modelFactory)
	{
		ArgumentNullException.ThrowIfNull(location);
		ArgumentNullException.ThrowIfNull(archiveModel);
		ArgumentNullException.ThrowIfNull(folderModel);
		ArgumentNullException.ThrowIfNull(mount);
		ArgumentNullException.ThrowIfNull(workspace);
		ArgumentNullException.ThrowIfNull(modelFactory);

		_location = location;
		_archiveModel = archiveModel;
		_folderModel = folderModel;
		_mount = mount;
		_workspace = workspace;
		_modelFactory = modelFactory;
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		await foreach (var item in _folderModel .GetItemsAsync(cancellationToken: cancellationToken) .ConfigureAwait(false))
		{
			yield return item;
		}
	}

	/// <inheritdoc />
	public async ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
		ArgumentNullException.ThrowIfNull(reference);

		if (reference.SourceId != _mount.ItemSource.SourceId)
		{
			return await _workspace.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
		}

		var coreModel = await _mount.ItemSource.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);

		return _modelFactory.Create(_mount.ItemSource, coreModel);
	}

	/// <inheritdoc />
	public async ValueTask<BrowseLocation?> GetParentLocationAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
		cancellationToken.ThrowIfCancellationRequested();

		if (!string.IsNullOrEmpty(_location.EntryPath))
		{
			return new ArchiveLocation(_location.Archive, ArchiveEntryPath.GetParent(_location.EntryPath));
		}

		if (_archiveModel.GetCoreModel() is not OwlCore.Storage.IStorableChild child)
		{
			return null;
		}

		var parent = await child.GetParentAsync(cancellationToken).ConfigureAwait(false);
		if (parent is null)
		{
			return null;
		}

		var source = GetSource(_archiveModel.Reference.SourceId);
		var parentModel = _modelFactory.Create(source, parent);

		try
		{
			if (parentModel is not IFolderModel)
			{
				throw new InvalidOperationException("The archive's storage parent is not a folder.");
			}

			return new FolderLocation(parentModel.Reference);
		}
		finally
		{
			await parentModel.DisposeAsync().ConfigureAwait(false);
		}
	}

	private IStorageSource GetSource(StorageSourceId sourceId)
	{
		var matches = _workspace.Sources.Where(source => source.SourceId == sourceId).Take(2).ToArray();

		return matches.Length switch
		{
			1 => matches[0],
			0 => throw new KeyNotFoundException($"Storage source '{sourceId}' is not registered."),
			_ => throw new InvalidOperationException($"Storage source '{sourceId}' is registered more than once."),
		};
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
		{
			return;
		}

		var errors = new List<Exception>();
		if (!ReferenceEquals(_folderModel, _archiveModel))
		{
			await TryDisposeAsync(_folderModel, errors).ConfigureAwait(false);
		}

		await TryDisposeAsync(_mount, errors).ConfigureAwait(false);
		await TryDisposeAsync(_archiveModel, errors).ConfigureAwait(false);
		GC.SuppressFinalize(this);

		if (errors.Count is 1)
		{
			throw errors[0];
		}

		if (errors.Count > 1)
		{
			throw new AggregateException("One or more archive browse resources could not be disposed.", errors);
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
