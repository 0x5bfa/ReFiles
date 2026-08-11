// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;

namespace Files.Core.Browsing;

/// <summary>
/// Handles one or more browse location types without adding UI navigation concepts.
/// </summary>
public interface IBrowseLocationHandler
{
	/// <summary>Determines whether this handler supports a location.</summary>
	/// <param name="location">The location to inspect.</param>
	/// <returns><see langword="true"/> when this handler can open the location.</returns>
	bool CanHandle(BrowseLocation location);

	/// <summary>Opens a location handled by this implementation.</summary>
	/// <param name="location">The location to open.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The opened browse context.</returns>
	ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default);
}
