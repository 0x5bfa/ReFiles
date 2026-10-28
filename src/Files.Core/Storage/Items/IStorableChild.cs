// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Threading;
using System.Threading.Tasks;

namespace Files.Core.Storage;

/// <summary>
/// Represents a storable resource that resides within a traversable folder structure.
/// </summary>
public interface IStorableChild : IStorable
{
	/// <summary>
	/// Gets the containing folder for this item, when one exists.
	/// </summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>An owned parent folder, or <see langword="null"/> when none exists in this source's hierarchy.</returns>
	/// <remarks>The caller must dispose a disposable result or transfer it to an owning model. The result borrows its storage source or mount.</remarks>
	Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default);
}
