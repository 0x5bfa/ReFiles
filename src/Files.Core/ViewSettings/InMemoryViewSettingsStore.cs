// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.ViewSettings;

/// <summary>Stores view settings for the lifetime of one Core runtime.</summary>
public sealed class InMemoryViewSettingsStore : IViewSettingsStore
{
	private readonly Lock _syncRoot = new();
	private readonly Dictionary<ViewSettingsScopeKey, BrowseViewSettingsOverride> _values = [];

	/// <summary>Gets the settings override stored for a view scope.</summary>
	/// <param name="scope">The stable view scope.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The stored override, or <see langword="null"/> when none exists.</returns>
	public ValueTask<BrowseViewSettingsOverride?> GetAsync(ViewSettingsScopeKey scope, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scope);

		cancellationToken.ThrowIfCancellationRequested();

		lock (_syncRoot)
		{
			return ValueTask.FromResult(_values.GetValueOrDefault(scope));
		}
	}

	/// <summary>Gets complete settings stored for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The stored settings applied to defaults, or <see langword="null"/> when none exists.</returns>
	public async ValueTask<BrowseViewSettings?> GetAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		var settingsOverride = await GetAsync(ViewSettingsScopeKey.ForLocation(location), cancellationToken).ConfigureAwait(false);
		if (settingsOverride is null)
		{
			return null;
		}

		return settingsOverride.Fields == ViewSettingsOverrideFields.All ? settingsOverride.Values : settingsOverride.ApplyTo(BrowseViewSettings.Default);
	}

	/// <summary>Stores a settings override for a view scope.</summary>
	/// <param name="scope">The stable view scope.</param>
	/// <param name="settingsOverride">The settings override to store.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	public ValueTask SetAsync(ViewSettingsScopeKey scope, BrowseViewSettingsOverride settingsOverride, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scope);

		ArgumentNullException.ThrowIfNull(settingsOverride);

		cancellationToken.ThrowIfCancellationRequested();

		lock (_syncRoot)
		{
			_values[scope] = settingsOverride;
		}

		return ValueTask.CompletedTask;
	}

	/// <summary>Atomically replaces selected fields in the settings override stored for a view scope.</summary>
	/// <param name="scope">The stable view scope.</param>
	/// <param name="fields">The fields to replace or clear.</param>
	/// <param name="replacement">Replacement values whose supplied fields must be a subset of <paramref name="fields"/>.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The stored override after the patch, or <see langword="null"/> when no fields remain.</returns>
	public ValueTask<BrowseViewSettingsOverride?> PatchAsync(ViewSettingsScopeKey scope, ViewSettingsOverrideFields fields, BrowseViewSettingsOverride replacement,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scope);

		ArgumentNullException.ThrowIfNull(replacement);

		cancellationToken.ThrowIfCancellationRequested();

		lock (_syncRoot)
		{
			var current = _values.GetValueOrDefault(scope) ?? new BrowseViewSettingsOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
			var updated = current.ReplaceFields(fields, replacement);
			if (updated.Fields == ViewSettingsOverrideFields.None)
			{
				_values.Remove(scope);

				return ValueTask.FromResult<BrowseViewSettingsOverride?>(null);
			}

			_values[scope] = updated;

			return ValueTask.FromResult<BrowseViewSettingsOverride?>(updated);
		}
	}

	/// <summary>Stores complete settings for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="settings">The complete settings to store.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	public ValueTask SetAsync(BrowseLocation location, BrowseViewSettings settings, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		ArgumentNullException.ThrowIfNull(settings);

		return SetAsync(ViewSettingsScopeKey.ForLocation(location), BrowseViewSettingsOverride.FromSettings(settings), cancellationToken);
	}

	/// <summary>Removes the settings override stored for a view scope.</summary>
	/// <param name="scope">The stable view scope.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns><see langword="true"/> when an override was removed.</returns>
	public ValueTask<bool> RemoveAsync(ViewSettingsScopeKey scope, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scope);

		cancellationToken.ThrowIfCancellationRequested();

		lock (_syncRoot)
		{
			return ValueTask.FromResult(_values.Remove(scope));
		}
	}

	/// <summary>Removes settings for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <returns><see langword="true"/> when settings were removed.</returns>
	public bool Remove(BrowseLocation location)
	{
		ArgumentNullException.ThrowIfNull(location);

		lock (_syncRoot)
		{
			return _values.Remove(ViewSettingsScopeKey.ForLocation(location));
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
