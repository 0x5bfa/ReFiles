// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

/// <summary>Creates search locations while preserving a provider's scope.</summary>
public interface IBrowseSearchProvider
{
	/// <summary>Determines whether the location supports searching.</summary>
	/// <param name="location">The current location.</param>
	/// <returns>Whether searching is supported.</returns>
	bool CanSearch(BrowseLocation location);

	/// <summary>Creates a search within the current scope.</summary>
	/// <param name="query">The nonempty query.</param>
	/// <param name="location">The current location.</param>
	/// <returns>The search location.</returns>
	BrowseLocation CreateSearch(string query, BrowseLocation location);
}
