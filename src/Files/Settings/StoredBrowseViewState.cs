// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ViewSettings;

namespace Files.Settings;

internal sealed class StoredBrowseViewState
{
	public int? LayoutMode { get; set; }

	public List<StoredBrowseViewColumnState>? Columns { get; set; }

	public string? SortPropertyId { get; set; }

	public int? SortDirection { get; set; }

	public double? ItemSize { get; set; }

	public string? GroupPropertyId { get; set; }

	public int? GroupDirection { get; set; }

	public BrowseViewSettings? TryCreateSettings()
	{
		var layoutMode = (ViewLayoutMode)(LayoutMode ?? (int)ViewLayoutMode.Details);
		if (!Enum.IsDefined(layoutMode))
		{
			return null;
		}

		var sortDirection = ToSortDirection(SortDirection);
		var groupDirection = ToSortDirection(GroupDirection);
		var columns = CreateColumns();
		var sortPropertyId = NormalizePropertyId(SortPropertyId);
		var groupPropertyId = NormalizePropertyId(GroupPropertyId);
		double? itemSize = ItemSize is { } size && double.IsFinite(size) && size > 0 ? size : null;

		try
		{
			return new BrowseViewSettings(layoutMode, columns, sortPropertyId, sortDirection, itemSize, groupPropertyId, groupDirection);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	public static StoredBrowseViewState FromSettings(BrowseViewSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		return new StoredBrowseViewState
		{
			LayoutMode = (int)settings.LayoutMode,
			Columns = settings.Columns.Select(StoredBrowseViewColumnState.FromSettings).ToList(),
			SortPropertyId = settings.SortPropertyId,
			SortDirection = (int)settings.SortDirection,
			ItemSize = settings.ItemSize,
			GroupPropertyId = settings.GroupPropertyId,
			GroupDirection = (int)settings.GroupDirection,
		};
	}

	private List<ViewColumnSettings> CreateColumns()
	{
		var result = new List<ViewColumnSettings>();
		var propertyIds = new HashSet<string>(StringComparer.Ordinal);
		var orders = new HashSet<int>();
		foreach (var column in Columns ?? [])
		{
			if (column is null || !column.IsValid || !propertyIds.Add(column.PropertyId) || !orders.Add(column.Order))
			{
				continue;
			}

			result.Add(new ViewColumnSettings(column.PropertyId, column.Width, column.Order, column.IsVisible));
		}

		return result;
	}

	private static ViewSortDirection ToSortDirection(int? value)
	{
		var direction = (ViewSortDirection)(value ?? (int)ViewSortDirection.Ascending);

		return Enum.IsDefined(direction) ? direction : ViewSortDirection.Ascending;
	}

	private static string? NormalizePropertyId(string? propertyId)
	{
		return string.IsNullOrWhiteSpace(propertyId) ? null : propertyId;
	}
}

internal sealed class StoredBrowseViewColumnState
{
	public string PropertyId { get; set; } = string.Empty;

	public double Width { get; set; }

	public int Order { get; set; }

	public bool IsVisible { get; set; }

	public bool IsValid => !string.IsNullOrWhiteSpace(PropertyId) && double.IsFinite(Width) && Width > 0 && Order >= 0;

	public static StoredBrowseViewColumnState FromSettings(ViewColumnSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		return new StoredBrowseViewColumnState
		{
			PropertyId = settings.PropertyId,
			Width = settings.Width,
			Order = settings.Order,
			IsVisible = settings.IsVisible,
		};
	}
}

internal sealed class ViewSettingsDocument
{
	public int SchemaVersion { get; set; }

	public Dictionary<string, StoredBrowseViewState?> Entries { get; set; } = new(StringComparer.Ordinal);
}
