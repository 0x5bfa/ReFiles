// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Storage;

/// <summary>
/// Provides roots and item resolution for one configured storage source.
/// </summary>
public interface IStorageSource : IAsyncDisposable
{
	/// <summary>Gets the stable identifier of this storage source.</summary>
	StorageSourceId SourceId { get; }

	/// <summary>Gets the source kind identifier.</summary>
	string SourceType { get; }

	/// <summary>Gets the display name of the source.</summary>
	string DisplayName { get; }

	/// <summary>Enumerates the root folders exposed by the source.</summary>
	/// <param name="cancellationToken">The token used to cancel enumeration.</param>
	/// <returns>An asynchronous sequence of root folders.</returns>
	IAsyncEnumerable<IFolder> GetRootsAsync(CancellationToken cancellationToken = default);

	/// <summary>Determines whether this source can resolve an address.</summary>
	/// <param name="address">The address to inspect.</param>
	/// <returns><see langword="true"/> when this source can resolve the address.</returns>
	bool CanResolve(StorageAddress address);

	/// <summary>Resolves an address to a storage item.</summary>
	/// <param name="address">The address to resolve.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved storage item.</returns>
	ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default);

	/// <summary>Resolves a stable item reference.</summary>
	/// <param name="reference">The reference to resolve.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved storage item.</returns>
	ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default);
}
