// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;
using Files.ViewModels;
using Files.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.PresentationTests;

[TestClass]
public sealed class DetailsRowRealizationTests
{
	[TestMethod]
	public void BindsColumnsToRealizedTemplateRoot()
	{
		var columns = new[]
		{
			new DetailsColumnViewModel("System.ItemNameDisplay", "Name", 180, WindowsShellColumnAlignment.Left, isStretch: true),
		};
		var row = new TestDetailsRowContent();

		var wasBound = DetailsRowRealization.TryBind(row, columns, out var realizedRow);

		Assert.IsTrue(wasBound);
		Assert.AreSame(row, realizedRow);
		Assert.AreSame(columns, row.Columns);
	}

	[TestMethod]
	public void IgnoresNonDetailsTemplateRoots()
	{
		var wasBound = DetailsRowRealization.TryBind(new object(), [], out var row);

		Assert.IsFalse(wasBound);
		Assert.IsNull(row);
	}

	private sealed class TestDetailsRowContent : IDetailsRowContent
	{
		public IReadOnlyList<DetailsColumnViewModel>? Columns { get; set; }

		public bool HasMeaningfulContent => Columns is { Count: > 0 };
	}
}
