// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.Storage;

namespace Files.Core.Data;

/// <summary>
/// Provides the UI-independent entry point for configured storage sources and Files item models.
/// </summary>
/// <remarks>
/// UI, CLI, and background hosts can use this contract without creating a window, tab, pane, or browse session.
/// </remarks>
public interface IStorageWorkspace : IAsyncDisposable
{
	/// <summary>
	/// Gets the configured storage sources.
	/// </summary>
	IReadOnlyList<IStorageSource> Sources { get; }

	/// <summary>
	/// Gets the root folder models exposed by a storage source.
	/// </summary>
	/// <param name="sourceId">The storage source identifier.</param>
	/// <param name="cancellationToken">The token used to cancel enumeration.</param>
	/// <returns>The root folder models owned by the caller.</returns>
	IAsyncEnumerable<IFolderModel> GetRootsAsync(StorageSourceId sourceId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Resolves an address with a specific storage source.
	/// </summary>
	/// <param name="sourceId">The storage source identifier.</param>
	/// <param name="address">The storage address to resolve.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved item model owned by the caller.</returns>
	ValueTask<IStorableModel> ResolveAsync(StorageSourceId sourceId, StorageAddress address, CancellationToken cancellationToken = default);

	/// <summary>
	/// Resolves an address with the single compatible storage source.
	/// </summary>
	/// <param name="address">The storage address to resolve.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved item model owned by the caller.</returns>
	ValueTask<IStorableModel> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default);

	/// <summary>
	/// Resolves a stable Files item reference.
	/// </summary>
	/// <param name="reference">The item reference to resolve.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved item model owned by the caller.</returns>
	ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default);
}
