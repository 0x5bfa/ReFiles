// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Core.Storage;

/// <summary>
/// Provides a direct name lookup path for a folder item.
/// </summary>
public interface IGetFirstByName : IFolder
{
	/// <summary>
	/// Gets the first child item with the specified name.
	/// </summary>
	/// <param name="name">The item name.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The matching child item.</returns>
	/// <exception cref="FileNotFoundException">The item was not found.</exception>
	Task<IStorableChild> GetFirstByNameAsync(string name, CancellationToken cancellationToken = default);
}
