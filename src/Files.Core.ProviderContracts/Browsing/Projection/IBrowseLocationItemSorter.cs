// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.ViewSettings;

namespace Files.Core.Browsing;

internal interface IBrowseLocationItemSorter
{
	ValueTask<IReadOnlyList<IStorableModel>?> SortItemsAsync(IReadOnlyList<IStorableModel> items, BrowseViewSettings settings, CancellationToken cancellationToken);
}
