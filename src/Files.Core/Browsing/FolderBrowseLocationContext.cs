// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;

namespace Files.Core.Browsing;

/// <summary>
/// Keeps a resolved folder model alive for the duration of a browse location.
/// </summary>
public sealed class FolderBrowseLocationContext : IBrowseLocationContext, IBrowseLocationItemResolver
{
	private readonly FolderLocation _location;

	private readonly IFolderModel _folderModel;

	private readonly IFilesDataRoot _dataRoot;

	private int _isDisposed;

	public BrowseLocation Location => _location;

	public IStorableModel LocationModel => _folderModel;

	public FolderBrowseLocationContext(FolderLocation location, IFolderModel folderModel, IFilesDataRoot dataRoot)
	{
		ArgumentNullException.ThrowIfNull(location);
		ArgumentNullException.ThrowIfNull(folderModel);
		ArgumentNullException.ThrowIfNull(dataRoot);

		_location = location;
		_folderModel = folderModel;
		_dataRoot = dataRoot;
	}

	public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		await foreach (var item in _folderModel.GetItemsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
		{
			yield return item;
		}
	}

	public ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
		ArgumentNullException.ThrowIfNull(reference);

		return _dataRoot.ResolveAsync(reference, cancellationToken);
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
		{
			await _folderModel.DisposeAsync().ConfigureAwait(false);
		}

		GC.SuppressFinalize(this);
	}
}
