// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

/// <summary>Provides property columns for a browse location.</summary>
public interface IBrowseColumnProvider
{
	/// <summary>Gets the available columns.</summary>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The columns, or null when no custom columns are available.</returns>
	ValueTask<BrowseColumnSet?> GetColumnsAsync(CancellationToken cancellationToken = default);
}
