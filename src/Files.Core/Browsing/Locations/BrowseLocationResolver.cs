// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

/// <summary>Resolves browse locations using the first registered handler that supports them.</summary>
public sealed class BrowseLocationResolver : IBrowseLocationResolver
{
	private readonly IReadOnlyList<IBrowseLocationHandler> _handlers;

	/// <summary>Initializes a location resolver.</summary>
	/// <param name="handlers">The handlers used to open browse locations.</param>
	public BrowseLocationResolver(IEnumerable<IBrowseLocationHandler> handlers)
	{
		ArgumentNullException.ThrowIfNull(handlers);

		var handlerArray = handlers.ToArray();
		if (handlerArray.Any(static handler => handler is null))
		{
			throw new ArgumentException("Browse location handlers cannot contain null values.", nameof(handlers));
		}

		_handlers = Array.AsReadOnly(handlerArray);
	}

	/// <summary>Opens a browse location through a compatible handler.</summary>
	/// <param name="location">The location to open.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The opened browse context.</returns>
	public ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		var handler = _handlers.FirstOrDefault(candidate => candidate.CanHandle(location))
			?? throw new InvalidOperationException($"No handler is registered for '{location.GetType().Name}'.");

		return handler.OpenAsync(location, cancellationToken);
	}
}
