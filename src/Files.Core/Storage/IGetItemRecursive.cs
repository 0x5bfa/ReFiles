// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Core.Storage;

/// <summary>
/// Provides a recursive item lookup path for a folder.
/// </summary>
public interface IGetItemRecursive : IFolder
{
	/// <summary>
	/// Searches this folder and its descendants for an item identifier.
	/// </summary>
	/// <param name="id">The item identifier.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The matching child item.</returns>
	/// <exception cref="FileNotFoundException">The item was not found.</exception>
	Task<IStorableChild> GetItemRecursiveAsync(string id, CancellationToken cancellationToken = default);
}
