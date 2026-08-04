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
	private readonly IStorageWorkspace _workspace;

	private int _isDisposed;

	/// <inheritdoc />
	public BrowseLocation Location { get; }

	/// <inheritdoc />
	public IStorableModel? LocationModel => null;

	/// <summary>Initializes a home browse context.</summary>
	/// <param name="location">The home location.</param>
	/// <param name="workspace">The storage workspace.</param>
	public HomeBrowseLocationContext(HomeLocation location, IStorageWorkspace workspace)
	{
		ArgumentNullException.ThrowIfNull(location);
		ArgumentNullException.ThrowIfNull(workspace);

		Location = location;
		_workspace = workspace;
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		foreach (var source in _workspace.Sources)
		{
			await foreach (var root in _workspace.GetRootsAsync(source.SourceId, cancellationToken).ConfigureAwait(false))
			{
				yield return root;
			}
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
	public ValueTask DisposeAsync()
	{
		Interlocked.Exchange(ref _isDisposed, 1);
		GC.SuppressFinalize(this);

		return ValueTask.CompletedTask;
	}
}
