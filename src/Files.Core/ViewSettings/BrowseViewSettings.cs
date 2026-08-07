// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>
/// Contains UI-agnostic presentation state for one browse location.
/// </summary>
public sealed record BrowseViewSettings
{
	/// <summary>Gets the default browse view settings.</summary>
	public static BrowseViewSettings Default { get; } = new();

	/// <summary>Gets the layout mode.</summary>
	public ViewLayoutMode LayoutMode { get; }

	/// <summary>Gets the configured columns.</summary>
	public IReadOnlyList<ViewColumnSettings> Columns { get; }

	/// <summary>Gets the property ID used to sort items.</summary>
	public string? SortPropertyId { get; }

	/// <summary>Gets the sort direction.</summary>
	public ViewSortDirection SortDirection { get; }

	/// <summary>Gets the preferred item size.</summary>
	public double? ItemSize { get; }

	/// <summary>Gets the property ID used to group items.</summary>
	public string? GroupPropertyId { get; }

	/// <summary>Gets the group direction.</summary>
	public ViewSortDirection GroupDirection { get; }

	/// <summary>Initializes browse view settings.</summary>
	/// <param name="layoutMode">The layout mode.</param>
	/// <param name="columns">The configured columns.</param>
	/// <param name="sortPropertyId">The property ID used to sort items.</param>
	/// <param name="sortDirection">The sort direction.</param>
	/// <param name="itemSize">The preferred item size.</param>
	/// <param name="groupPropertyId">The property ID used to group items.</param>
	/// <param name="groupDirection">The group direction.</param>
	public BrowseViewSettings(
		ViewLayoutMode layoutMode = ViewLayoutMode.Details,
		IEnumerable<ViewColumnSettings>? columns = null,
		string? sortPropertyId = null,
		ViewSortDirection sortDirection = ViewSortDirection.Ascending,
		double? itemSize = null,
		string? groupPropertyId = null,
		ViewSortDirection groupDirection = ViewSortDirection.Ascending)
	{
		if (layoutMode is not ViewLayoutMode.Details and not ViewLayoutMode.List and not ViewLayoutMode.Grid and not ViewLayoutMode.Columns)
		{
			throw new ArgumentOutOfRangeException(nameof(layoutMode));
		}

		if (sortDirection is not ViewSortDirection.Ascending and not ViewSortDirection.Descending)
		{
			throw new ArgumentOutOfRangeException(nameof(sortDirection));
		}

		if (groupDirection is not ViewSortDirection.Ascending and not ViewSortDirection.Descending)
		{
			throw new ArgumentOutOfRangeException(nameof(groupDirection));
		}

		if (sortPropertyId is not null)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(sortPropertyId);
		}

		if (groupPropertyId is not null)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(groupPropertyId);
		}

		if (itemSize is { } size && (!double.IsFinite(size) || size <= 0))
		{
			throw new ArgumentOutOfRangeException(nameof(itemSize));
		}

		var columnArray = (columns ?? []).ToArray();
		if (columnArray.Any(static column => column is null))
		{
			throw new ArgumentException("View columns cannot contain null values.", nameof(columns));
		}

		if (columnArray.Select(static column => column.PropertyId).Distinct(StringComparer.Ordinal).Count() != columnArray.Length)
		{
			throw new ArgumentException("View column property IDs must be unique.", nameof(columns));
		}

		if (columnArray.Select(static column => column.Order).Distinct().Count() != columnArray.Length)
		{
			throw new ArgumentException("View column orders must be unique.", nameof(columns));
		}

		LayoutMode = layoutMode;
		Columns = Array.AsReadOnly(columnArray);
		SortPropertyId = sortPropertyId;
		SortDirection = sortDirection;
		ItemSize = itemSize;
		GroupPropertyId = groupPropertyId;
		GroupDirection = groupDirection;
	}
}
