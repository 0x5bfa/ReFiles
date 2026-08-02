// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.ViewSettings;

/// <summary>
/// Stores view settings for the lifetime of one Core runtime.
/// </summary>
public sealed class InMemoryViewSettingsStore : IViewSettingsStore
{
	private readonly object syncRoot = new();
	private readonly Dictionary<BrowseLocation, BrowseViewSettings> values = [];

	public ValueTask<BrowseViewSettings?> GetAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);
		cancellationToken.ThrowIfCancellationRequested();

		lock (syncRoot)
		{
			return ValueTask.FromResult(values.GetValueOrDefault(location));
		}
	}

	public ValueTask SetAsync(BrowseLocation location, BrowseViewSettings settings, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);
		ArgumentNullException.ThrowIfNull(settings);
		cancellationToken.ThrowIfCancellationRequested();

		lock (syncRoot)
		{
			values[location] = settings;
		}

		return ValueTask.CompletedTask;
	}

	public bool Remove(BrowseLocation location)
	{
		ArgumentNullException.ThrowIfNull(location);

		lock (syncRoot)
		{
			return values.Remove(location);
		}
	}

	public void Clear()
	{
		lock (syncRoot)
		{
			values.Clear();
		}
	}
}
