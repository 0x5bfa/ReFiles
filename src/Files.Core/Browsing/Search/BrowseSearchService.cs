// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

/// <summary>Routes search intent to the first compatible provider.</summary>
public sealed class BrowseSearchService : IBrowseSearchProvider
{
	private readonly IReadOnlyList<IBrowseSearchProvider> _providers;

	/// <summary>Initializes the configured search providers.</summary>
	/// <param name="providers">The providers in routing order.</param>
	public BrowseSearchService(IEnumerable<IBrowseSearchProvider> providers)
	{
		ArgumentNullException.ThrowIfNull(providers);

		_providers = providers.ToArray();
		if (_providers.Any(static provider => provider is null))
		{
			throw new ArgumentException("Search providers cannot contain null values.", nameof(providers));
		}
	}

	/// <inheritdoc />
	public bool CanSearch(BrowseLocation location)
	{
		ArgumentNullException.ThrowIfNull(location);

		return _providers.Any(provider => provider.CanSearch(location));
	}

	/// <inheritdoc />
	public BrowseLocation CreateSearch(string query, BrowseLocation location)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);

		ArgumentNullException.ThrowIfNull(location);

		var provider = _providers.FirstOrDefault(candidate => candidate.CanSearch(location))
			?? throw new NotSupportedException("The current location does not support searching.");

		return provider.CreateSearch(query, location);
	}
}
