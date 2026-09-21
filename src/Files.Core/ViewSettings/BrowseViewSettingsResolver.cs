// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>Resolves persisted view state against the capabilities of a location.</summary>
public static class BrowseViewSettingsResolver
{
	/// <summary>Resolves persisted settings against location capabilities.</summary>
	/// <param name="settings">The persisted settings.</param>
	/// <param name="capabilities">The available location capabilities.</param>
	/// <returns>Settings that can be applied by the current location.</returns>
	public static BrowseViewSettings Resolve(BrowseViewSettings settings, BrowseViewCapabilities? capabilities)
	{
		ArgumentNullException.ThrowIfNull(settings);

		if (capabilities is null)
		{
			return settings;
		}

		var columns = ResolveColumns(settings, capabilities);
		var sortPropertyId = capabilities.CanSortBy(settings.SortPropertyId) ? settings.SortPropertyId : null;
		var groupPropertyId = capabilities.CanGroupBy(settings.GroupPropertyId) ? settings.GroupPropertyId : null;
		if (ReferenceEquals(columns, settings.Columns) && string.Equals(sortPropertyId, settings.SortPropertyId, StringComparison.Ordinal) && string.Equals(groupPropertyId, settings.GroupPropertyId, StringComparison.Ordinal))
		{

			return settings;
		}

		return new BrowseViewSettings(settings.LayoutMode, columns, sortPropertyId, settings.SortDirection, settings.ItemSize, groupPropertyId, settings.GroupDirection);
	}

	/// <summary>Prepares settings for display before asynchronous capability discovery completes.</summary>
	/// <param name="settings">The persisted settings.</param>
	/// <returns>Settings safe to use while capability-dependent grouping is unresolved.</returns>
	public static BrowseViewSettings DeferCapabilityDependentSettings(BrowseViewSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		if (settings.GroupPropertyId is null)
		{
			return settings;
		}

		return new BrowseViewSettings(settings.LayoutMode, settings.Columns, settings.SortPropertyId, settings.SortDirection, settings.ItemSize, groupDirection: settings.GroupDirection);
	}

	private static IReadOnlyList<ViewColumnSettings> ResolveColumns(BrowseViewSettings settings, BrowseViewCapabilities capabilities)
	{
		if (settings.Columns.Count is 0)
		{
			return capabilities.DefaultSettings.Columns;
		}

		var result = new List<ViewColumnSettings>(settings.Columns.Count);
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var hasChanges = false;
		foreach (var column in settings.Columns.OrderBy(static column => column.Order))
		{
			if (capabilities.IsColumnAvailable(column.PropertyId) && seen.Add(column.PropertyId))
			{
				result.Add(column);
				continue;
			}

			hasChanges = true;
		}
		var nextOrder = settings.Columns.Count is 0 ? 0 : settings.Columns.Max(static column => column.Order) + 1;
		foreach (var defaultColumn in capabilities.DefaultSettings.Columns.OrderBy(static column => column.Order))
		{
			if (capabilities.IsColumnAvailable(defaultColumn.PropertyId) && seen.Add(defaultColumn.PropertyId))
			{
				result.Add(new ViewColumnSettings(defaultColumn.PropertyId, defaultColumn.Width, nextOrder++));
				hasChanges = true;
			}
		}

		if (!hasChanges && result.Count == settings.Columns.Count)
		{

			return settings.Columns;
		}

		return result.Count is not 0 ? Array.AsReadOnly(result.ToArray()) : capabilities.DefaultSettings.Columns;
	}
}
