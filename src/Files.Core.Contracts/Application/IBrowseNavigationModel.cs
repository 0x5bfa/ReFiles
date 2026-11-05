// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.Core.Browsing;

/// <summary>Provides consumer access to BrowseNavigationModel state and operations.</summary>
public interface IBrowseNavigationModel
{
	/// <summary>Gets the hierarchy of the current location.</summary>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>Navigation values that do not retain provider resources.</returns>
	Task<IReadOnlyList<BrowseNavigationEntry>> GetBreadcrumbsAsync(CancellationToken cancellationToken = default);

	/// <summary>Gets child locations for a navigation target.</summary>
	/// <param name="location">The parent location.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The child locations and encoded icons.</returns>
	Task<IReadOnlyList<BrowseNavigationEntry>> GetChildrenAsync(BrowseLocation location, CancellationToken cancellationToken = default);
}
