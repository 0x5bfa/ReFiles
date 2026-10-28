// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.ViewSettings;

namespace Files.Core.Browsing;

/// <summary>Provides optional columns and native ordering for a folder.</summary>
public interface IFolderBrowseMetadata : IBrowseColumnProvider
{
	/// <summary>Orders models using provider semantics.</summary>
	/// <param name="items">The borrowed models.</param>
	/// <param name="settings">The requested sort settings.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The ordered models, or null to use the common ordering.</returns>
	ValueTask<IReadOnlyList<IStorableModel>?> SortItemsAsync(IReadOnlyList<IStorableModel> items, BrowseViewSettings settings, CancellationToken cancellationToken);
}
