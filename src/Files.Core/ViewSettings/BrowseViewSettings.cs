// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>
/// Contains UI-agnostic presentation state for one browse location.
/// </summary>
public sealed record BrowseViewSettings
{
	public BrowseViewSettings(
		ViewLayoutMode layoutMode = ViewLayoutMode.Details,
		IEnumerable<ViewColumnSettings>? columns = null,
		string? sortPropertyId = null,
		ViewSortDirection sortDirection = ViewSortDirection.Ascending,
		double? itemSize = null)
	{
		if (layoutMode is not ViewLayoutMode.Details
			and not ViewLayoutMode.List
			and not ViewLayoutMode.Grid
			and not ViewLayoutMode.Columns)
		{
			throw new ArgumentOutOfRangeException(nameof(layoutMode));
		}

		if (sortDirection is not ViewSortDirection.Ascending
			and not ViewSortDirection.Descending)
		{
			throw new ArgumentOutOfRangeException(nameof(sortDirection));
		}

		if (sortPropertyId is not null)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(sortPropertyId);
		}

		if (itemSize is { } size
			&& (!double.IsFinite(size) || size <= 0))
		{
			throw new ArgumentOutOfRangeException(nameof(itemSize));
		}

		var columnArray = (columns ?? []).ToArray();
		if (columnArray.Any(static column => column is null))
		{
			throw new ArgumentException("View columns cannot contain null values.", nameof(columns));
		}

		if (columnArray
			.Select(static column => column.PropertyId)
			.Distinct(StringComparer.Ordinal)
			.Count() != columnArray.Length)
		{
			throw new ArgumentException("View column property IDs must be unique.", nameof(columns));
		}

		if (columnArray
			.Select(static column => column.Order)
			.Distinct()
			.Count() != columnArray.Length)
		{
			throw new ArgumentException("View column orders must be unique.", nameof(columns));
		}

		LayoutMode = layoutMode;
		Columns = Array.AsReadOnly(columnArray);
		SortPropertyId = sortPropertyId;
		SortDirection = sortDirection;
		ItemSize = itemSize;
	}

	public static BrowseViewSettings Default { get; } = new();

	public ViewLayoutMode LayoutMode { get; }

	public IReadOnlyList<ViewColumnSettings> Columns { get; }

	public string? SortPropertyId { get; }

	public ViewSortDirection SortDirection { get; }

	public double? ItemSize { get; }
}
