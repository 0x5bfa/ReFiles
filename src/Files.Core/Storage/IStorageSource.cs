// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Storage;

/// <summary>
/// Provides roots and item resolution for one configured storage source.
/// </summary>
public interface IStorageSource : IAsyncDisposable
{
	StorageSourceId SourceId { get; }

	string SourceType { get; }

	string DisplayName { get; }

	IAsyncEnumerable<IFolder> GetRootsAsync(CancellationToken cancellationToken = default);

	bool CanResolve(StorageAddress address);

	ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default);

	ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default);
}
