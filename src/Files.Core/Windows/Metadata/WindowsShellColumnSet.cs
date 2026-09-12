// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains all column metadata returned by one Windows Shell folder.
/// </summary>
public sealed class WindowsShellColumnSet
{
	/// <summary>Gets all columns exposed by the folder in Shell column order.</summary>
	public IReadOnlyList<WindowsShellColumn> All { get; }

	/// <summary>Gets the columns marked for the Shell's default Details view.</summary>
	public IReadOnlyList<WindowsShellColumn> DefaultVisible { get; }

	/// <summary>Gets the default Shell sort column index, or <see langword="null"/> when unavailable.</summary>
	public int? DefaultSortColumnIndex { get; }

	/// <summary>Gets the default Shell display column index, or <see langword="null"/> when unavailable.</summary>
	public int? DefaultDisplayColumnIndex { get; }

	internal WindowsShellColumnSet(IEnumerable<WindowsShellColumn> columns, int? defaultSortColumnIndex, int? defaultDisplayColumnIndex)
	{
		ArgumentNullException.ThrowIfNull(columns);

		var columnArray = columns.ToArray();
		All = Array.AsReadOnly(columnArray);
		DefaultVisible = Array.AsReadOnly(columnArray.Where(static column => column.IsVisibleByDefault && !column.IsHidden).ToArray());
		DefaultSortColumnIndex = defaultSortColumnIndex;
		DefaultDisplayColumnIndex = defaultDisplayColumnIndex;
	}
}
