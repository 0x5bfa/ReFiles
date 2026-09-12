// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Core.Storage;

/// <summary>
/// Represents a folder that supports creating and deleting child items.
/// </summary>
public interface IModifiableFolder : IMutableFolder
{
	/// <summary>
	/// Deletes an item from this folder.
	/// </summary>
	/// <param name="item">The item to delete.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	/// <exception cref="FileNotFoundException">The item is not present in this folder.</exception>
	Task DeleteAsync(IStorableChild item, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates or opens a child folder with the specified name.
	/// </summary>
	/// <param name="name">The name of the child folder.</param>
	/// <param name="overwrite">Whether an existing item may be opened.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The created or opened child folder.</returns>
	Task<IChildFolder> CreateFolderAsync(string name, bool overwrite = default, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates or opens a child file with the specified name.
	/// </summary>
	/// <param name="name">The name of the child file.</param>
	/// <param name="overwrite">Whether an existing item may be opened.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The created or opened child file.</returns>
	Task<IChildFile> CreateFileAsync(string name, bool overwrite = default, CancellationToken cancellationToken = default);
}
