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
	private readonly Dictionary<ViewSettingsKey, BrowseViewSettings> _values = [];
	private readonly Dictionary<BrowseLocation, BrowseViewSettings> _fallbackValues = [];

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
			return ValueTask.FromResult(ViewSettingsKey.TryForLocation(location, out var key)
				? _values.GetValueOrDefault(key)
				: _fallbackValues.GetValueOrDefault(location));
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
			if (ViewSettingsKey.TryForLocation(location, out var key))
			{
				_values[key] = settings;
			}
			else
			{
				_fallbackValues[location] = settings;
			}
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
			return ViewSettingsKey.TryForLocation(location, out var key) ? _values.Remove(key) : _fallbackValues.Remove(location);
		}
	}

	/// <summary>Removes all stored view settings.</summary>
	public void Clear()
	{
		lock (_syncRoot)
		{
			_values.Clear();
			_fallbackValues.Clear();
		}
	}
}
