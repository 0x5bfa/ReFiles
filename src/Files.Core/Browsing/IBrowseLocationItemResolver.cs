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
	ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default);
}
