// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Storage.Archives;

/// <summary>
/// Owns one selected archive backend and every item exposed by that backend.
/// </summary>
public interface IArchiveMount : IAsyncDisposable
{
	string BackendId { get; }

	StorableReference Archive { get; }

	IStorageSource ItemSource { get; }

	IFolder Root { get; }

	ValueTask<IStorable> ResolveAsync(string entryPath, CancellationToken cancellationToken = default);
}
