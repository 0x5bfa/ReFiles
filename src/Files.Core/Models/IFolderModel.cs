// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Models;

/// <summary>
/// Represents a Files application model for an enumerable folder.
/// </summary>
public interface IFolderModel : IStorableModel
{
	/// <summary>
	/// Enumerates child item models owned by the caller.
	/// </summary>
	/// <param name="type">The item types to include.</param>
	/// <param name="cancellationToken">The token used to cancel enumeration.</param>
	/// <returns>The child item models.</returns>
	IAsyncEnumerable<IStorableModel> GetItemsAsync(StorableType type = StorableType.All, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the parent folder model when the storage source exposes one.
	/// </summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The parent folder model owned by the caller, or <see langword="null"/> for a root.</returns>
	ValueTask<IFolderModel?> GetParentAsync(CancellationToken cancellationToken = default);
}
