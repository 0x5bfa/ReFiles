// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Files.UITests.Views;

/// <summary>
/// Verifies selection behavior exposed by the list-based table rows host.
/// </summary>
[TestClass]
public sealed class ListViewTableRowsHostTests
{
	/// <summary>
	/// Verifies that replacing the items source does not publish teardown-driven selection changes.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task ItemsSourceReplacementSuppressesSelectionNotification()
	{
		var items = new[] { "one", "two" };
		var rowsHost = new ListViewTableRowsHost { ItemsSource = items };
		var selectionChangeCount = 0;
		rowsHost.SelectionChanged += (_, _) => selectionChangeCount++;

		rowsHost.View.SelectedItem = items[0];
		Assert.AreEqual(1, selectionChangeCount);

		selectionChangeCount = 0;
		rowsHost.ItemsSource = null;
		Assert.AreEqual(0, selectionChangeCount);
		await Task.CompletedTask;
	}
}
