// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.ViewSettings;

/// <summary>
/// Stores view settings for the lifetime of one Core runtime.
/// </summary>
public sealed class InMemoryViewSettingsStore : IViewSettingsStore
{
	private readonly Lock _syncRoot = new();
	private readonly Dictionary<BrowseLocation, BrowseViewSettings> _values = [];

	/// <summary>Gets the settings stored for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The stored settings, or <see langword="null"/> when none exist.</returns>
	public ValueTask<BrowseViewSettings?> GetAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);
		cancellationToken.ThrowIfCancellationRequested();

		lock (_syncRoot)
		{
			return ValueTask.FromResult(_values.GetValueOrDefault(location));
		}
	}

	/// <summary>Stores settings for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="settings">The settings to store.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	public ValueTask SetAsync(BrowseLocation location, BrowseViewSettings settings, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);
		ArgumentNullException.ThrowIfNull(settings);
		cancellationToken.ThrowIfCancellationRequested();

		lock (_syncRoot)
		{
			_values[location] = settings;
		}

		return ValueTask.CompletedTask;
	}

	/// <summary>Removes settings for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <returns><see langword="true"/> when settings were removed.</returns>
	public bool Remove(BrowseLocation location)
	{
		ArgumentNullException.ThrowIfNull(location);

		lock (_syncRoot)
		{
			return _values.Remove(location);
		}
	}

	/// <summary>Removes all stored view settings.</summary>
	public void Clear()
	{
		lock (_syncRoot)
		{
			_values.Clear();
		}
	}
}
