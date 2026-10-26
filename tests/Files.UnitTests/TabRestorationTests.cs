// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Sessions;

namespace Files.UnitTests;

/// <summary>Verifies complete tab restoration and failure ownership.</summary>
[TestClass]
public sealed class TabRestorationTests
{
	/// <summary>Duplicates and reopens every pane, history cursor, and layout.</summary>
	/// <param name="orientation">The layout to restore.</param>
	/// <param name="activeIndex">The active pane to restore.</param>
	/// <returns>The test task.</returns>
	[TestMethod]
	[DataRow(PaneSplitOrientation.Vertical, 0)]
	[DataRow(PaneSplitOrientation.Horizontal, 1)]
	public async Task DuplicateAndReopenPreserveBothHistories(PaneSplitOrientation orientation, int activeIndex)
	{
		await using var window = new WindowSession(new BrowsePaneSessionFactory(new TestBrowseLocationResolver([])));
		var original = await window.OpenTabAsync(new TagLocation("first"));
		var first = (BrowsePaneSession)original.Panes[0].Content;
		await first.NavigateAsync(new TagLocation("second"));
		await first.NavigateAsync(new TagLocation("third"));
		await first.GoBackAsync();
		var split = await original.OpenSplitAsync(orientation);
		var second = (BrowsePaneSession)split.Content;
		await second.NavigateAsync(new TagLocation("other"));
		original.SetActivePane(original.Panes[activeIndex].Id);
		var histories = original.Panes.Select(pane => ((BrowsePaneSession)pane.Content).History.Capture()).ToArray();
		var originalPaneIds = original.Panes.Select(pane => pane.Id).ToArray();

		var duplicate = await window.DuplicateTabAsync(original.Id);
		Assert.IsNotNull(duplicate);
		Verify(duplicate, orientation, activeIndex, histories, originalPaneIds);
		await ((BrowsePaneSession)duplicate.Panes[0].Content).GoForwardAsync();
		Assert.AreEqual(new TagLocation("second"), first.Location);

		await window.CloseTabAsync(original.Id);
		Assert.IsTrue(window.CanReopenTab);
		var reopened = await window.ReopenTabAsync();
		Assert.IsNotNull(reopened);
		Assert.IsFalse(window.CanReopenTab);
		Verify(reopened, orientation, activeIndex, histories, originalPaneIds);
		Assert.AreNotEqual(duplicate.Id, reopened.Id);
	}

	/// <summary>A failed second pane leaves no partial tab and retains the closed state for retry.</summary>
	/// <returns>The test task.</returns>
	[TestMethod]
	public async Task FailedReopenDisposesAllPartialPanesAndRetainsState()
	{
		var resolver = new TestBrowseLocationResolver([]);
		await using var window = new WindowSession(new BrowsePaneSessionFactory(resolver));
		var original = await window.OpenTabAsync(new TagLocation("first"));
		await original.OpenSplitAsync(PaneSplitOrientation.Horizontal);
		await window.CloseTabAsync(original.Id);
		var previousContexts = resolver.OpenedContexts.Count;
		resolver.ContextOpened = _ => resolver.Exception = new IOException("second pane unavailable");
		await Assert.ThrowsAsync<IOException>(async () => await window.ReopenTabAsync());
		Assert.IsEmpty(window.Tabs);
		Assert.IsTrue(window.CanReopenTab);
		Assert.AreEqual(previousContexts + 2, resolver.OpenedContexts.Count);
		Assert.IsTrue(resolver.OpenedContexts.Skip(previousContexts).All(context => context.IsDisposed));
		resolver.ContextOpened = null;
		resolver.Exception = null;
		var restored = await window.ReopenTabAsync();
		Assert.IsNotNull(restored);
		Assert.AreEqual(2, restored.Panes.Count);
		Assert.IsFalse(window.CanReopenTab);
	}

	private static void Verify(TabSession tab, PaneSplitOrientation orientation, int activeIndex, BrowseNavigationHistorySnapshot[] histories, Guid[] previousIds)
	{
		Assert.AreEqual(2, tab.Panes.Count);
		Assert.AreEqual(orientation, tab.SplitOrientation);
		Assert.AreEqual(tab.Panes[activeIndex], tab.ActivePane);
		for (var index = 0; index < tab.Panes.Count; index++)
		{
			Assert.AreNotEqual(previousIds[index], tab.Panes[index].Id);
			var content = (BrowsePaneSession)tab.Panes[index].Content;
			CollectionAssert.AreEqual(histories[index].Entries.ToArray(), content.History.Entries.ToArray());
			Assert.AreEqual(histories[index].CurrentIndex, content.History.CurrentIndex);
			Assert.AreEqual(histories[index].Current, content.Location);
		}
	}
}
