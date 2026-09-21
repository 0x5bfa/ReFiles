// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Windows;

namespace Files.Core.ViewSettings;

/// <summary>
/// Describes one property column that a browse location can render.
/// </summary>
public sealed record BrowseViewColumnCapabilities
{
	/// <summary>Gets the stable property identifier.</summary>
	public string PropertyId { get; }

	/// <summary>Gets the default width in device-independent pixels.</summary>
	public double DefaultWidth { get; }

	/// <summary>Gets a value indicating whether the column is visible by default.</summary>
	public bool IsVisibleByDefault { get; }

	/// <summary>Gets a value indicating whether the property can be used for sorting.</summary>
	public bool CanSort { get; }

	/// <summary>Gets a value indicating whether the property can be used for grouping.</summary>
	public bool CanGroup { get; }

	/// <summary>Initializes column capabilities.</summary>
	/// <param name="propertyId">The stable property identifier.</param>
	/// <param name="defaultWidth">The default width in device-independent pixels.</param>
	/// <param name="isVisibleByDefault">Whether the column is visible by default.</param>
	/// <param name="canSort">Whether the property can be used for sorting.</param>
	/// <param name="canGroup">Whether the property can be used for grouping.</param>
	public BrowseViewColumnCapabilities(string propertyId, double defaultWidth, bool isVisibleByDefault, bool canSort = true, bool canGroup = true)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);

		if (!double.IsFinite(defaultWidth) || defaultWidth <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(defaultWidth));
		}

		PropertyId = propertyId;
		DefaultWidth = defaultWidth;
		IsVisibleByDefault = isVisibleByDefault;
		CanSort = canSort;
		CanGroup = canGroup;
	}
}

/// <summary>
/// Describes the presentation properties supported by a browse location.
/// </summary>
public sealed class BrowseViewCapabilities
{
	private readonly IReadOnlyDictionary<string, BrowseViewColumnCapabilities> _columnsById;

	/// <summary>Gets the columns exposed by the location.</summary>
	public IReadOnlyList<BrowseViewColumnCapabilities> Columns { get; }

	/// <summary>Gets the default settings for the location.</summary>
	public BrowseViewSettings DefaultSettings { get; }

	/// <summary>Initializes browse view capabilities.</summary>
	/// <param name="columns">The columns exposed by the location.</param>
	/// <param name="defaultSettings">The optional default settings for the location.</param>
	public BrowseViewCapabilities(IEnumerable<BrowseViewColumnCapabilities> columns, BrowseViewSettings? defaultSettings = null)
	{
		ArgumentNullException.ThrowIfNull(columns);

		var columnArray = columns.ToArray();
		if (columnArray.Any(static column => column is null))
		{
			throw new ArgumentException("View capabilities cannot contain null columns.", nameof(columns));
		}

		var columnsById = new Dictionary<string, BrowseViewColumnCapabilities>(StringComparer.Ordinal);
		foreach (var column in columnArray)
		{
			if (!columnsById.TryAdd(column.PropertyId, column))
			{
				throw new ArgumentException($"View capability property ID '{column.PropertyId}' is duplicated.", nameof(columns));
			}
		}

		Columns = Array.AsReadOnly(columnArray);
		_columnsById = columnsById;
		DefaultSettings = defaultSettings ?? new BrowseViewSettings(columns: CreateDefaultColumns(columnArray));
	}

	/// <summary>Creates capabilities from Windows Shell column metadata.</summary>
	/// <param name="columnSet">The Windows Shell column set.</param>
	/// <returns>The corresponding browse view capabilities.</returns>
	public static BrowseViewCapabilities FromWindowsShell(WindowsShellColumnSet columnSet)
	{
		ArgumentNullException.ThrowIfNull(columnSet);

		var columns = columnSet.All
			.Where(static column => !column.IsHidden)
			.GroupBy(static column => column.PropertyId, StringComparer.Ordinal)
			.Select(static group =>
			{
				var column = group.First();
				return new BrowseViewColumnCapabilities(column.PropertyId, GetDefaultColumnWidth(column), column.IsVisibleByDefault, canGroup: column.CanGroup);
			})
			.ToArray();
		var defaultColumns = CreateDefaultColumns(columnSet.DefaultVisible);
		var defaultSortPropertyId = columnSet.DefaultSortColumnIndex is { } sortIndex && (uint)sortIndex < (uint)columnSet.All.Count
			? columnSet.All[sortIndex].PropertyId
			: null;
		var defaultSettings = new BrowseViewSettings(columns: defaultColumns, sortPropertyId: defaultSortPropertyId);

		return new BrowseViewCapabilities(columns, defaultSettings);
	}

	/// <summary>Determines whether a property is available as a column.</summary>
	/// <param name="propertyId">The property identifier.</param>
	/// <returns><see langword="true"/> when the property is available.</returns>
	public bool IsColumnAvailable(string? propertyId)
	{
		return propertyId is not null && _columnsById.ContainsKey(propertyId);
	}

	/// <summary>Determines whether a property can be used for sorting.</summary>
	/// <param name="propertyId">The property identifier.</param>
	/// <returns><see langword="true"/> when sorting is supported.</returns>
	public bool CanSortBy(string? propertyId)
	{
		return propertyId is not null && _columnsById.TryGetValue(propertyId, out var column) && column.CanSort;
	}

	/// <summary>Determines whether a property can be used for grouping.</summary>
	/// <param name="propertyId">The property identifier.</param>
	/// <returns><see langword="true"/> when grouping is supported.</returns>
	public bool CanGroupBy(string? propertyId)
	{
		return propertyId is not null && _columnsById.TryGetValue(propertyId, out var column) && column.CanGroup;
	}

	private static IReadOnlyList<ViewColumnSettings> CreateDefaultColumns(IEnumerable<BrowseViewColumnCapabilities> columns)
	{
		var result = new List<ViewColumnSettings>();
		foreach (var column in columns.Where(static column => column.IsVisibleByDefault))
		{
			result.Add(new ViewColumnSettings(column.PropertyId, column.DefaultWidth, result.Count));
		}

		return Array.AsReadOnly(result.ToArray());
	}

	private static IReadOnlyList<ViewColumnSettings> CreateDefaultColumns(IEnumerable<WindowsShellColumn> columns)
	{
		var result = new List<ViewColumnSettings>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var column in columns)
		{
			if (seen.Add(column.PropertyId))
			{
				result.Add(new ViewColumnSettings(column.PropertyId, GetDefaultColumnWidth(column), result.Count));
			}
		}

		return Array.AsReadOnly(result.ToArray());
	}

	private static double GetDefaultColumnWidth(WindowsShellColumn column)
	{
		return column.HeaderWidthCharacters is > 0 ? column.HeaderWidthCharacters * 8d : 120d;
	}
}
