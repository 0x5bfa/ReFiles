// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using BenchmarkDotNet.Attributes;
using Files.Controls;
using Microsoft.UI.Xaml;
using System.ComponentModel;

namespace Files.Benchmarks;

[MemoryDiagnoser]
public class TableViewColumnLayoutBenchmarks
{
	private ITableViewColumn[] _columns = [];

	[Params(8, 32, 128, 512)]
	public int ColumnCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_columns = Enumerable.Range(0, ColumnCount).Select(index => (ITableViewColumn)new BenchmarkColumn($"column-{index}", 72 + index % 160)).ToArray();
	}

	[Benchmark]
	public TableViewColumnLayout ResolveLayout()
	{
		return TableViewColumnLayout.Create(_columns, 1920);
	}

	private sealed class BenchmarkColumn(string id, double width) : ITableViewColumn
	{
		public string Id { get; } = id;

		public string Header => Id;

		public double Width { get; set; } = width;

		public double MinWidth => 48;

		public double MaxWidth => 1200;

		public TextAlignment TextAlignment => Microsoft.UI.Xaml.TextAlignment.Left;

		public bool IsPrimary => false;

		public bool CanResize => true;

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
