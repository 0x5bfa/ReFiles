// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;

namespace Files.Core.Browsing;

/// <summary>
/// Owns the model and enumeration lifetime for one active browse location.
/// </summary>
public interface IBrowseLocationContext : IAsyncDisposable
{
	BrowseLocation Location { get; }

	IStorableModel? LocationModel { get; }

	IAsyncEnumerable<IStorableModel> GetItemsAsync(CancellationToken cancellationToken = default);
}
