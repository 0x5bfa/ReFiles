// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.Storage;

namespace Files.Core.Browsing;

/// <summary>
/// Resolves item models for incremental changes within a browse context.
/// </summary>
public interface IBrowseLocationItemResolver
{
	/// <summary>Resolves an item reference in the current location.</summary>
	/// <param name="reference">The item reference to resolve.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved item model.</returns>
	ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default);
}
