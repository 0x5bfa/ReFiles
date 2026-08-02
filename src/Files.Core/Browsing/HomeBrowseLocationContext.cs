// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;

namespace Files.Core.Browsing;

/// <summary>
/// Enumerates the roots of every configured source without creating a synthetic storable.
/// </summary>
public sealed class HomeBrowseLocationContext
	: IBrowseLocationContext, IBrowseLocationItemResolver
{
	private readonly IFilesDataRoot _dataRoot;

	private int _isDisposed;

	public BrowseLocation Location { get; }

	public IStorableModel? LocationModel => null;

	public HomeBrowseLocationContext(HomeLocation location, IFilesDataRoot dataRoot)
	{
		ArgumentNullException.ThrowIfNull(location);
		ArgumentNullException.ThrowIfNull(dataRoot);

		Location = location;
		_dataRoot = dataRoot;
	}

	public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		foreach (var source in _dataRoot.Sources)
		{
			await foreach (var root in _dataRoot .GetRootsAsync(source.SourceId, cancellationToken) .ConfigureAwait(false))
			{
				yield return root;
			}
		}
	}

	public ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
		ArgumentNullException.ThrowIfNull(reference);

		return _dataRoot.ResolveAsync(reference, cancellationToken);
	}

	public ValueTask DisposeAsync()
	{
		Interlocked.Exchange(ref _isDisposed, 1);
		GC.SuppressFinalize(this);

		return ValueTask.CompletedTask;
	}
}
