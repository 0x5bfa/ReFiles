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
	/// <returns>The containing parent folder, or <see langword="null"/> when none exists.</returns>
	Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default);
}
