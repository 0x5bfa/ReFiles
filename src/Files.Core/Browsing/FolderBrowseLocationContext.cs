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
	private readonly FolderLocation location;
	private readonly IFolderModel folderModel;
	private readonly IFilesDataRoot dataRoot;
	private int isDisposed;

	public FolderBrowseLocationContext(
		FolderLocation location,
		IFolderModel folderModel,
		IFilesDataRoot dataRoot)
	{
		ArgumentNullException.ThrowIfNull(location);
		ArgumentNullException.ThrowIfNull(folderModel);
		ArgumentNullException.ThrowIfNull(dataRoot);

		this.location = location;
		this.folderModel = folderModel;
		this.dataRoot = dataRoot;
	}

	public BrowseLocation Location => location;

	public IStorableModel LocationModel => folderModel;

	public async IAsyncEnumerable<IStorableModel> GetItemsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);

		await foreach (var item in folderModel.GetItemsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
		{
			yield return item;
		}
	}

	public ValueTask<IStorableModel> ResolveAsync(
		StorableReference reference,
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);
		ArgumentNullException.ThrowIfNull(reference);

		return dataRoot.ResolveAsync(reference, cancellationToken);
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) == 0)
		{
			await folderModel.DisposeAsync().ConfigureAwait(false);
		}

		GC.SuppressFinalize(this);
	}
}
