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

	private readonly IFilesDataRoot _dataRoot;

	private int _isDisposed;

	public BrowseLocation Location => _location;

	public IStorableModel LocationModel => _folderModel;

	public bool CanGetParent =>
		!string.IsNullOrEmpty(_location.EntryPath) ||
		_archiveModel.CoreModel is OwlCore.Storage.IStorableChild;

	public ArchiveBrowseLocationContext(ArchiveLocation location, IStorableModel archiveModel, IFolderModel folderModel, IArchiveMount mount, IFilesDataRoot dataRoot)
	{
		ArgumentNullException.ThrowIfNull(location);
		ArgumentNullException.ThrowIfNull(archiveModel);
		ArgumentNullException.ThrowIfNull(folderModel);
		ArgumentNullException.ThrowIfNull(mount);
		ArgumentNullException.ThrowIfNull(dataRoot);

		_location = location;
		_archiveModel = archiveModel;
		_folderModel = folderModel;
		_mount = mount;
		_dataRoot = dataRoot;
	}

	public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		await foreach (var item in _folderModel .GetItemsAsync(cancellationToken: cancellationToken) .ConfigureAwait(false))
		{
			yield return item;
		}
	}

	public async ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
		ArgumentNullException.ThrowIfNull(reference);

		if (reference.SourceId != _mount.ItemSource.SourceId)
		{
			return await _dataRoot.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
		}

		var coreModel = await _mount.ItemSource.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);

		return _dataRoot.ModelFactory.Create(_mount.ItemSource, coreModel);
	}

	public async ValueTask<BrowseLocation?> GetParentLocationAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
		cancellationToken.ThrowIfCancellationRequested();

		if (!string.IsNullOrEmpty(_location.EntryPath))
		{
			return new ArchiveLocation(_location.Archive, ArchiveEntryPath.GetParent(_location.EntryPath));
		}

		if (_archiveModel.CoreModel is not OwlCore.Storage.IStorableChild child)
		{
			return null;
		}

		var parent = await child.GetParentAsync(cancellationToken).ConfigureAwait(false);
		if (parent is null)
		{
			return null;
		}

		var source = _dataRoot.GetSource(_archiveModel.Reference.SourceId);
		var parentModel = _dataRoot.ModelFactory.Create(source, parent);

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
