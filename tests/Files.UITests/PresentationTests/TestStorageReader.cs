// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.Storage;

namespace Files.Core.Data;

internal static class TestStorageReaderExtensions
{
	internal static IStorageReader AsConsumerReader(this IStorageWorkspace workspace)
	{
		return new TestStorageReader(workspace);
	}
}

internal sealed class TestStorageReader(IStorageWorkspace workspace) : IStorageReader
{
	public IReadOnlyList<StorageSourceDescriptor> Sources => workspace.Sources.Select(static source => new StorageSourceDescriptor(source.SourceId, source.SourceType, source.DisplayName)).ToArray();

	public IAsyncEnumerable<IFolderModel> GetRootsAsync(StorageSourceId sourceId, CancellationToken cancellationToken = default)
	{
		return workspace.GetRootsAsync(sourceId, cancellationToken);
	}

	public ValueTask<IStorableModel> ResolveAsync(StorageSourceId sourceId, StorageAddress address, CancellationToken cancellationToken = default)
	{
		return workspace.ResolveAsync(sourceId, address, cancellationToken);
	}

	public ValueTask<IStorableModel> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default)
	{
		return workspace.ResolveAsync(address, cancellationToken);
	}

	public ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		return workspace.ResolveAsync(reference, cancellationToken);
	}
}
