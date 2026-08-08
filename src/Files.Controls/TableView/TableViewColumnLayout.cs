// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Controls;

/// <summary>
/// Stores resolved widths and offsets shared by table headers and realized rows.
/// </summary>
public sealed class TableViewColumnLayout
{
	private readonly double[] _offsets;
	private readonly double[] _widths;

	/// <summary>Gets an empty layout.</summary>
	public static TableViewColumnLayout Empty { get; } = new([], [], 0, 0);

	/// <summary>Gets the number of resolved columns.</summary>
	public int Count => _widths.Length;

	/// <summary>Gets the combined width of all columns.</summary>
	public double ColumnsWidth { get; }

	/// <summary>Gets the row width, including trailing viewport space.</summary>
	public double ContentWidth { get; }

	private TableViewColumnLayout(double[] offsets, double[] widths, double columnsWidth, double contentWidth)
	{
		_offsets = offsets;
		_widths = widths;
		ColumnsWidth = columnsWidth;
		ContentWidth = contentWidth;
	}

	/// <summary>
	/// Resolves a column layout.
	/// </summary>
	/// <param name="columns">The columns to resolve.</param>
	/// <param name="viewportWidth">The available viewport width.</param>
	/// <returns>The resolved immutable layout.</returns>
	public static TableViewColumnLayout Create(IReadOnlyList<ITableViewColumn> columns, double viewportWidth)
	{
		ArgumentNullException.ThrowIfNull(columns);

		if (!double.IsFinite(viewportWidth) || viewportWidth < 0)
		{
			viewportWidth = 0;
		}

		if (columns.Count is 0)
		{
			return viewportWidth is 0 ? Empty : new([], [], 0, viewportWidth);
		}

		var offsets = new double[columns.Count];
		var widths = new double[columns.Count];
		var offset = 0d;
		for (var index = 0; index < columns.Count; index++)
		{
			var column = columns[index] ?? throw new ArgumentException("Columns cannot contain null values.", nameof(columns));
			var minWidth = NormalizeMinimum(column.MinWidth);
			var maxWidth = NormalizeMaximum(column.MaxWidth, minWidth);
			var width = double.IsFinite(column.Width) ? Math.Clamp(column.Width, minWidth, maxWidth) : minWidth;
			offsets[index] = offset;
			widths[index] = width;
			offset += width;
		}

		return new(offsets, widths, offset, Math.Max(offset, viewportWidth));
	}

	/// <summary>Gets the horizontal offset for a column.</summary>
	public double GetOffset(int index)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);

		if (index >= _offsets.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(index));
		}

		return _offsets[index];
	}

	/// <summary>Gets the resolved width for a column.</summary>
	public double GetWidth(int index)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);

		if (index >= _widths.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(index));
		}

		return _widths[index];
	}

	private static double NormalizeMinimum(double value)
	{
		return double.IsFinite(value) && value >= 0 ? value : 0;
	}

	private static double NormalizeMaximum(double value, double minimum)
	{
		return double.IsNaN(value) || value < minimum ? minimum : value;
	}
}
