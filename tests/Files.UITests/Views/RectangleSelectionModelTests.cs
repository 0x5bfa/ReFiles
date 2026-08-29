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
	private const int DefaultStressIterationCount = 2_000;
	private const int MaximumStressIterationCount = 100_000;

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

	/// <summary>
	/// Verifies randomized replace, extend, and toggle sequences against set algebra.
	/// </summary>
	[TestMethod]
	[TestCategory("Stress")]
	public void RandomizedModifierSequencesMatchSetAlgebra()
	{
		var random = new Random(0x5E1EC7);
		var universe = Enumerable.Range(0, 256).Select(static index => (object)index).ToArray();
		var iterationCount = ReadStressIterationCount();
		for (var iteration = 0; iteration < iterationCount; iteration++)
		{
			var baseline = CreateRandomSet(random, universe);
			var intersection = CreateRandomSet(random, universe);
			var mode = (RectangleSelectionMode)(iteration % 3);
			var model = new RectangleSelectionModel(baseline, mode);
			var actual = model.GetSelection(intersection);
			var expected = GetExpectedSelection(baseline, intersection, mode);

			Assert.IsTrue(expected.SetEquals(actual), $"Selection mismatch at iteration {iteration} for mode {mode}.");
		}
	}

	private static HashSet<object> CreateRandomSet(Random random, IReadOnlyList<object> universe)
	{
		var result = new HashSet<object>();
		var count = random.Next(0, 65);
		for (var index = 0; index < count; index++)
		{
			result.Add(universe[random.Next(universe.Count)]);
		}

		return result;
	}

	private static HashSet<object> GetExpectedSelection(IEnumerable<object> baseline, IEnumerable<object> intersection, RectangleSelectionMode mode)
	{
		if (mode is RectangleSelectionMode.Replace)
		{
			return intersection.ToHashSet();
		}

		var result = baseline.ToHashSet();
		if (mode is RectangleSelectionMode.Extend)
		{
			result.UnionWith(intersection);
		}
		else if (mode is RectangleSelectionMode.Toggle)
		{
			result.SymmetricExceptWith(intersection);
		}
		else
		{
			throw new InvalidOperationException($"Unsupported rectangle selection mode '{mode}'.");
		}

		return result;
	}

	private static int ReadStressIterationCount()
	{
		var value = Environment.GetEnvironmentVariable("FILES_RECTANGLE_SELECTION_STRESS_ITERATIONS");
		if (string.IsNullOrWhiteSpace(value))
		{
			return DefaultStressIterationCount;
		}

		if (!int.TryParse(value, out var iterationCount) || iterationCount < 1 || iterationCount > MaximumStressIterationCount)
		{
			throw new InvalidOperationException($"FILES_RECTANGLE_SELECTION_STRESS_ITERATIONS must be between 1 and {MaximumStressIterationCount}.");
		}

		return iterationCount;
	}
}
