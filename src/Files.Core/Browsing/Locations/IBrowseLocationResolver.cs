// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

/// <summary>Opens browse locations and returns their contexts.</summary>
public interface IBrowseLocationResolver
{
	/// <summary>Opens a browse location.</summary>
	/// <param name="location">The location to open.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The opened browse context.</returns>
	ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default);
}
