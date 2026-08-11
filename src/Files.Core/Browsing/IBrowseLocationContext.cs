// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;

namespace Files.Core.Browsing;

/// <summary>
/// Owns the model and enumeration lifetime for one active browse location.
/// </summary>
public interface IBrowseLocationContext : IAsyncDisposable
{
	/// <summary>Gets the location represented by this context.</summary>
	BrowseLocation Location { get; }

	/// <summary>Gets the model for the location, if one exists.</summary>
	IStorableModel? LocationModel { get; }

	/// <summary>Enumerates the items in the location.</summary>
	/// <param name="cancellationToken">The token used to cancel enumeration.</param>
	/// <returns>An asynchronous sequence of items.</returns>
	IAsyncEnumerable<IStorableModel> GetItemsAsync(CancellationToken cancellationToken = default);
}
