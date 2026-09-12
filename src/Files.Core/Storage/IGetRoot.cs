// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Threading;
using System.Threading.Tasks;

namespace Files.Core.Storage;

/// <summary>
/// Provides a direct root lookup path for a storage child.
/// </summary>
public interface IGetRoot : IStorableChild
{
	/// <summary>
	/// Gets the root folder for this storage hierarchy.
	/// </summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The root folder, or <see langword="null"/> when this item is the root.</returns>
	Task<IFolder?> GetRootAsync(CancellationToken cancellationToken = default);
}
