// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

/// <summary>Contains the property columns available at a browse location.</summary>
public sealed class BrowseColumnSet
{
	/// <summary>Gets all columns in provider order.</summary>
	public IReadOnlyList<BrowseColumn> All { get; }
	/// <summary>Gets the default visible columns.</summary>
	public IReadOnlyList<BrowseColumn> DefaultVisible { get; }
	/// <summary>Gets the default sort column index, if available.</summary>
	public int? DefaultSortColumnIndex { get; }

	/// <summary>Initializes a set of property columns.</summary>
	/// <param name="columns">The available columns.</param>
	/// <param name="defaultSortColumnIndex">The default sort column index.</param>
	public BrowseColumnSet(IEnumerable<BrowseColumn> columns, int? defaultSortColumnIndex = null)
	{
		ArgumentNullException.ThrowIfNull(columns);

		var items = columns.ToArray();
		if (items.Any(static column => column is null))
		{
			throw new ArgumentException("Columns cannot contain null items.", nameof(columns));
		}

		All = Array.AsReadOnly(items);
		DefaultVisible = Array.AsReadOnly(items.Where(static column => column.IsVisibleByDefault && !column.IsHidden).ToArray());
		DefaultSortColumnIndex = defaultSortColumnIndex;
	}
}
