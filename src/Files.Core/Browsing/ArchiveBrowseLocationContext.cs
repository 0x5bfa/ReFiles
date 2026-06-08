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
public sealed class ArchiveBrowseLocationContext
	: IBrowseLocationContext,
		IBrowseLocationItemResolver,
		IBrowseLocationParentResolver
{
	private readonly ArchiveLocation location;
	private readonly IStorableModel archiveModel;
	private readonly IFolderModel folderModel;
	private readonly IArchiveMount mount;
	private readonly IFilesDataRoot dataRoot;
	private int isDisposed;

	public ArchiveBrowseLocationContext(
		ArchiveLocation location,
		IStorableModel archiveModel,
		IFolderModel folderModel,
		IArchiveMount mount,
		IFilesDataRoot dataRoot)
	{
		ArgumentNullException.ThrowIfNull(location);
		ArgumentNullException.ThrowIfNull(archiveModel);
		ArgumentNullException.ThrowIfNull(folderModel);
		ArgumentNullException.ThrowIfNull(mount);
		ArgumentNullException.ThrowIfNull(dataRoot);

		this.location = location;
		this.archiveModel = archiveModel;
		this.folderModel = folderModel;
		this.mount = mount;
		this.dataRoot = dataRoot;
	}

	public BrowseLocation Location => location;

	public IStorableModel LocationModel => folderModel;

	public bool CanGetParent =>
		!string.IsNullOrEmpty(location.EntryPath)
		|| archiveModel.CoreModel is OwlCore.Storage.IStorableChild;

	public async IAsyncEnumerable<IStorableModel> GetItemsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(
			Volatile.Read(ref isDisposed) != 0,
			this);

		await foreach (var item in folderModel
			.GetItemsAsync(cancellationToken: cancellationToken)
			.ConfigureAwait(false))
		{
			yield return item;
		}
	}

	public async ValueTask<IStorableModel> ResolveAsync(
		StorableReference reference,
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(
			Volatile.Read(ref isDisposed) != 0,
			this);
		ArgumentNullException.ThrowIfNull(reference);

		if (reference.SourceId != mount.ItemSource.SourceId)
		{
			return await dataRoot
				.ResolveAsync(reference, cancellationToken)
				.ConfigureAwait(false);
		}

		var coreModel = await mount.ItemSource
			.ResolveAsync(reference, cancellationToken)
			.ConfigureAwait(false);
		return dataRoot.ModelFactory.Create(
			mount.ItemSource,
			coreModel);
	}

	public async ValueTask<BrowseLocation?> GetParentLocationAsync(
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(
			Volatile.Read(ref isDisposed) != 0,
			this);
		cancellationToken.ThrowIfCancellationRequested();

		if (!string.IsNullOrEmpty(location.EntryPath))
		{
			return new ArchiveLocation(
				location.Archive,
				ArchiveEntryPath.GetParent(
					location.EntryPath));
		}

		if (archiveModel.CoreModel
			is not OwlCore.Storage.IStorableChild child)
		{
			return null;
		}

		var parent = await child
			.GetParentAsync(cancellationToken)
			.ConfigureAwait(false);
		if (parent is null)
		{
			return null;
		}

		var source = dataRoot.GetSource(
			archiveModel.Reference.SourceId);
		var parentModel = dataRoot.ModelFactory.Create(
			source,
			parent);
		try
		{
			if (parentModel is not IFolderModel)
			{
				throw new InvalidOperationException(
					"The archive's storage parent is not a folder.");
			}

			return new FolderLocation(
				parentModel.Reference);
		}
		finally
		{
			await parentModel
				.DisposeAsync()
				.ConfigureAwait(false);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) != 0)
		{
			return;
		}

		var errors = new List<Exception>();
		if (!ReferenceEquals(folderModel, archiveModel))
		{
			await TryDisposeAsync(
				folderModel,
				errors).ConfigureAwait(false);
		}

		await TryDisposeAsync(
			mount,
			errors).ConfigureAwait(false);
		await TryDisposeAsync(
			archiveModel,
			errors).ConfigureAwait(false);
		GC.SuppressFinalize(this);

		if (errors.Count is 1)
		{
			throw errors[0];
		}

		if (errors.Count > 1)
		{
			throw new AggregateException(
				"One or more archive browse resources could not be disposed.",
				errors);
		}
	}

	private static async ValueTask TryDisposeAsync(
		IAsyncDisposable disposable,
		ICollection<Exception> errors)
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
