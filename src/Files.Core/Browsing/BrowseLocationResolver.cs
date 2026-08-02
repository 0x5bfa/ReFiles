// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

public sealed class BrowseLocationResolver : IBrowseLocationResolver
{
	private readonly IReadOnlyList<IBrowseLocationHandler> handlers;

	public BrowseLocationResolver(IEnumerable<IBrowseLocationHandler> handlers)
	{
		ArgumentNullException.ThrowIfNull(handlers);
		var handlerArray = handlers.ToArray();
		if (handlerArray.Any(static handler => handler is null))
		{
			throw new ArgumentException("Browse location handlers cannot contain null values.", nameof(handlers));
		}

		this.handlers = Array.AsReadOnly(handlerArray);
	}

	public ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		var handler = handlers.FirstOrDefault(candidate => candidate.CanHandle(location))
			?? throw new InvalidOperationException($"No handler is registered for '{location.GetType().Name}'.");

		return handler.OpenAsync(location, cancellationToken);
	}
}
