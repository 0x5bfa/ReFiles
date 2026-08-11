// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.ComponentModel;

namespace Files.PresentationTests;

/// <summary>
/// Verifies table column layout offsets and width constraints.
/// </summary>
[TestClass]
public sealed class TableViewColumnLayoutTests
{
	/// <summary>
	/// Verifies that offsets are resolved without stretching columns.
	/// </summary>
	[TestMethod]
	public void ResolvesOffsetsWithoutStretchingColumns()
	{
		var columns = new ITableViewColumn[]
		{
			new TestColumn("name", 180),
			new TestColumn("type", 120),
			new TestColumn("date", 140),
		};

		var layout = TableViewColumnLayout.Create(columns, 640);

		Assert.AreEqual(3, layout.Count);
		Assert.AreEqual(0, layout.GetOffset(0));
		Assert.AreEqual(180, layout.GetOffset(1));
		Assert.AreEqual(300, layout.GetOffset(2));
		Assert.AreEqual(440, layout.ColumnsWidth);
		Assert.AreEqual(640, layout.ContentWidth);
	}

	/// <summary>
	/// Verifies that column widths are clamped to their constraints.
	/// </summary>
	[TestMethod]
	public void ClampsWidthsToColumnConstraints()
	{
		var columns = new ITableViewColumn[]
		{
			new TestColumn("small", 10, 48, 200),
			new TestColumn("large", 500, 48, 320),
		};

		var layout = TableViewColumnLayout.Create(columns, 0);

		Assert.AreEqual(48, layout.GetWidth(0));
		Assert.AreEqual(320, layout.GetWidth(1));
		Assert.AreEqual(368, layout.ColumnsWidth);
	}

	/// <summary>
	/// Verifies that repeated large layouts stay within the allocation budget.
	/// </summary>
	[TestMethod]
	public void RepeatedLargeLayoutsStayWithinAllocationBudget()
	{
		var columns = Enumerable.Range(0, 256).Select(index => (ITableViewColumn)new TestColumn($"column-{index}", 96 + index % 32)).ToArray();
		_ = TableViewColumnLayout.Create(columns, 1920);
		var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

		for (var iteration = 0; iteration < 1_000; iteration++)
		{
			_ = TableViewColumnLayout.Create(columns, 1920);
		}

		var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
		Assert.IsLessThan(6_000_000, allocatedBytes, $"Allocated {allocatedBytes:N0} bytes while resolving 1,000 large layouts.");
	}

	private sealed class TestColumn(string id, double width, double minWidth = 0, double maxWidth = double.PositiveInfinity) : ITableViewColumn
	{
		public string Id { get; } = id;

		public object Header => Id;

		public DataTemplate? HeaderTemplate => null;

		public double Width { get; set; } = width;

		public double MinWidth { get; } = minWidth;

		public double MaxWidth { get; } = maxWidth;

		public TextAlignment TextAlignment => Microsoft.UI.Xaml.TextAlignment.Left;

		public bool IsPrimary => false;

		public bool CanResize => true;

		public bool CanReorder => true;

		public bool CanSort => true;

		public bool CanGroup => true;

		public DataTemplate? CellTemplate => null;

		public event PropertyChangedEventHandler? PropertyChanged
		{
			add { }
			remove { }
		}
	}
}
