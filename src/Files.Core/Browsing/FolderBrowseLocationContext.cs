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

	private readonly IStorageWorkspace _workspace;

	private int _isDisposed;

	/// <inheritdoc />
	public BrowseLocation Location => _location;

	/// <inheritdoc />
	public IStorableModel LocationModel => _folderModel;

	/// <summary>Initializes a folder browse context and takes ownership of the folder model.</summary>
	/// <param name="location">The folder location.</param>
	/// <param name="folderModel">The folder model.</param>
	/// <param name="workspace">The storage workspace.</param>
	public FolderBrowseLocationContext(FolderLocation location, IFolderModel folderModel, IStorageWorkspace workspace)
	{
		ArgumentNullException.ThrowIfNull(location);
		ArgumentNullException.ThrowIfNull(folderModel);
		ArgumentNullException.ThrowIfNull(workspace);

		_location = location;
		_folderModel = folderModel;
		_workspace = workspace;
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		await foreach (var item in _folderModel.GetItemsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
		{
			yield return item;
		}
	}

	/// <inheritdoc />
	public ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
		ArgumentNullException.ThrowIfNull(reference);

		return _workspace.ResolveAsync(reference, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
		{
			await _folderModel.DisposeAsync().ConfigureAwait(false);
		}

		GC.SuppressFinalize(this);
	}
}
