// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;

namespace Files.Core.Browsing;

/// <summary>Owns one archive application model and the active browse folder.</summary>
public sealed class ArchiveBrowseLocationContext : IBrowseLocationContext, IBrowseLocationItemResolver, IBrowseLocationParentResolver
{
	private readonly ArchiveLocation _location;
	private readonly ArchiveModel _archive;
	private readonly IFolderModel _folder;
	private readonly IStorageWorkspace _workspace;
	private readonly BrowseLocation? _parent;
	private readonly Lock _disposalLock = new();
	private Task? _disposeTask;
	private bool _isDisposed;

	/// <inheritdoc />
	public BrowseLocation Location => _location;

	/// <inheritdoc />
	public IStorableModel LocationModel => _folder;

	/// <inheritdoc />
	public bool CanGetParent => _parent is not null;

	/// <summary>Takes ownership of an archive model and a folder borrowing its lifetime.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="archive">The owned archive model.</param>
	/// <param name="folder">The owned active folder.</param>
	/// <param name="workspace">The borrowed workspace for non-archive references.</param>
	/// <param name="parent">The parent navigation location, when one exists.</param>
	public ArchiveBrowseLocationContext(ArchiveLocation location, ArchiveModel archive, IFolderModel folder, IStorageWorkspace workspace, BrowseLocation? parent)
	{
		ArgumentNullException.ThrowIfNull(location);

		ArgumentNullException.ThrowIfNull(archive);

		ArgumentNullException.ThrowIfNull(folder);

		ArgumentNullException.ThrowIfNull(workspace);

		_location = location;
		_archive = archive;
		_folder = folder;
		_workspace = workspace;
		_parent = parent;
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		await foreach (var item in _folder.GetItemsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
		{
			yield return item;
		}
	}

	/// <summary>Enumerates an owned set of child models from another folder in the same archive view.</summary>
	/// <param name="location">The requested location in this archive.</param>
	/// <param name="type">The item kinds to include.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>Owned child models borrowing this context's archive lifetime.</returns>
	internal async IAsyncEnumerable<IStorableModel> GetItemsAsync(ArchiveLocation location, StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		ArgumentNullException.ThrowIfNull(location);

		if (location.Locator != _location.Locator)
		{
			throw new ArgumentException("The location belongs to another archive.", nameof(location));
		}

		await using var folder = await _archive.OpenFolderAsync(location.EntryPath, cancellationToken).ConfigureAwait(false);
		await foreach (var item in folder.GetItemsAsync(type, cancellationToken).ConfigureAwait(false))
		{
			yield return item;
		}
	}

	/// <inheritdoc />
	public ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return reference.SourceId == _archive.Root.Reference.SourceId ? _archive.ResolveAsync(reference, cancellationToken) : _workspace.ResolveAsync(reference, cancellationToken);
	}

	/// <inheritdoc />
	public ValueTask<BrowseLocation?> GetParentLocationAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult(_parent);
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			_isDisposed = true;
			_disposeTask ??= DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		try
		{
			await _folder.DisposeAsync().ConfigureAwait(false);
		}
		finally
		{
			await _archive.DisposeAsync().ConfigureAwait(false);
		}
	}
}
