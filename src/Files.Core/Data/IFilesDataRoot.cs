// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.Storage;

namespace Files.Core.Data;

/// <summary>
/// Root of the storage-backed Files application model graph.
/// </summary>
public interface IFilesDataRoot : IAsyncDisposable
{
	IReadOnlyList<IStorageSource> Sources { get; }

	IStorableModelFactory ModelFactory { get; }

	IStorageSource GetSource(StorageSourceId sourceId);

	IAsyncEnumerable<IFolderModel> GetRootsAsync(StorageSourceId sourceId, CancellationToken cancellationToken = default);

	ValueTask<IStorableModel> ResolveAsync(
		StorageSourceId sourceId,
		StorageAddress address,
		CancellationToken cancellationToken = default);

	ValueTask<IStorableModel> ResolveAsync(
		StorageAddress address,
		CancellationToken cancellationToken = default);

	ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default);
}
