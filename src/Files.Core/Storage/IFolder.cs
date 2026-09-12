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
	/// <returns>An asynchronous sequence of child items.</returns>
	IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, CancellationToken cancellationToken = default);
}
