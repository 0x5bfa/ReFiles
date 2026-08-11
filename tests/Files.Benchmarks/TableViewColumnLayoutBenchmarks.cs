// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using BenchmarkDotNet.Attributes;
using Files.Controls;
using Microsoft.UI.Xaml;
using System.ComponentModel;

namespace Files.Benchmarks;

/// <summary>
/// Measures table column layout resolution performance.
/// </summary>
[MemoryDiagnoser]
public class TableViewColumnLayoutBenchmarks
{
	private ITableViewColumn[] _columns = [];

	/// <summary>
	/// Gets or sets the number of columns used by the benchmark.
	/// </summary>
	[Params(8, 32, 128, 512)]
	public int ColumnCount { get; set; }

	/// <summary>
	/// Creates the columns used by the layout benchmark.
	/// </summary>
	[GlobalSetup]
	public void Setup()
	{
		_columns = Enumerable.Range(0, ColumnCount).Select(index => (ITableViewColumn)new BenchmarkColumn($"column-{index}", 72 + index % 160)).ToArray();
	}

	/// <summary>
	/// Measures resolving the layout for the configured columns.
	/// </summary>
	/// <returns>The resolved table column layout.</returns>
	[Benchmark]
	public TableViewColumnLayout ResolveLayout()
	{
		return TableViewColumnLayout.Create(_columns, 1920);
	}

	private sealed class BenchmarkColumn(string id, double width) : ITableViewColumn
	{
		public string Id { get; } = id;

		public object Header => Id;

		public DataTemplate? HeaderTemplate => null;

		public double Width { get; set; } = width;

		public double MinWidth => 48;

		public double MaxWidth => 1200;

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
