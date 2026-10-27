// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Generic;
using System.Threading;

namespace Files.Core.Storage;

/// <summary>
/// Represents a folder in a storage source.
/// </summary>
public interface IFolder : IStorable
{
	/// <summary>
	/// Enumerates the items in this folder.
	/// </summary>
	/// <param name="type">The kinds of items to include.</param>
	/// <param name="cancellationToken">The token used to cancel enumeration.</param>
	/// <returns>Child items transferred to the caller one at a time. The caller must dispose disposable items or transfer them to an owning model.</returns>
	/// <remarks>Keep the storage source or mount alive while enumerating or using the returned items. Stopping enumeration does not dispose items already yielded.</remarks>
	IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, CancellationToken cancellationToken = default);
}
