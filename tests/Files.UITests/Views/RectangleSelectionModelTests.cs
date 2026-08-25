// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UITests.Views;

/// <summary>
/// Verifies rectangle-selection modifier behavior.
/// </summary>
[TestClass]
public sealed class RectangleSelectionModelTests
{
	/// <summary>
	/// Verifies that an unmodified rectangle replaces the original selection and remains reversible while dragging.
	/// </summary>
	[TestMethod]
	public void ReplaceSelectionTracksCurrentIntersection()
	{
		var model = new RectangleSelectionModel(["one", "two"], RectangleSelectionMode.Replace);

		CollectionAssert.AreEquivalent(new object[] { "three", "four" }, model.GetSelection(["three", "four"]).ToArray());
		CollectionAssert.AreEquivalent(new object[] { "three" }, model.GetSelection(["three"]).ToArray());
	}

	/// <summary>
	/// Verifies that Shift preserves the original selection while adding current intersections.
	/// </summary>
	[TestMethod]
	public void ExtendSelectionPreservesBaseline()
	{
		var model = new RectangleSelectionModel(["one", "two"], RectangleSelectionMode.Extend);

		CollectionAssert.AreEquivalent(new object[] { "one", "two", "three" }, model.GetSelection(["three"]).ToArray());
		CollectionAssert.AreEquivalent(new object[] { "one", "two" }, model.GetSelection([]).ToArray());
	}

	/// <summary>
	/// Verifies that Ctrl toggles current intersections against the original selection.
	/// </summary>
	[TestMethod]
	public void ToggleSelectionUsesStableBaseline()
	{
		var model = new RectangleSelectionModel(["one", "two"], RectangleSelectionMode.Toggle);

		CollectionAssert.AreEquivalent(new object[] { "one", "three" }, model.GetSelection(["two", "three"]).ToArray());
		CollectionAssert.AreEquivalent(new object[] { "one", "two", "three" }, model.GetSelection(["three"]).ToArray());
	}
}
