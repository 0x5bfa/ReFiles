// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Sessions;
using Files.Core.Browsing;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for session behavior.
/// </summary>
[TestClass]
public sealed class SessionTests
{
	/// <summary>
	/// Test case: pane navigation commits history and drops forward branch.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task PaneNavigationCommitsHistoryAndDropsForwardBranch()
	{
		var resolver = new TestBrowseLocationResolver([]);
		var paneFactory = new BrowsePaneSessionFactory(resolver, historyCapacity: 4);
		await using var paneOwner = paneFactory.Create();
		var pane = GetBrowsePane(paneOwner);
		var home = HomeLocation.Instance;
		var search = new SearchLocation("first");
		var tag = new TagLocation("tag");
		var branch = new SearchLocation("branch");

		await pane.NavigateAsync(home);
		await pane.NavigateAsync(search);
		await pane.NavigateAsync(tag);

		Assert.AreEqual(tag, pane.Location);
		Assert.IsTrue(pane.CanGoBack);
		Assert.IsFalse(pane.CanGoForward);
		Assert.IsTrue(await pane.GoBackAsync());
		Assert.AreEqual(search, pane.Location);
		Assert.IsTrue(pane.CanGoForward);

		await pane.NavigateAsync(branch);

		CollectionAssert.AreEqual(new BrowseLocation[] {home, search, branch}, pane.History.Entries.ToArray());
		Assert.AreEqual(branch, pane.History.Current);
		Assert.IsFalse(pane.CanGoForward);
	}

	/// <summary>
	/// Test case: pane restores bounded history around current entry.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task PaneRestoresBoundedHistoryAroundCurrentEntry()
	{
		var resolver = new TestBrowseLocationResolver([]);
		var paneFactory = new BrowsePaneSessionFactory(resolver, historyCapacity: 3);
		await using var paneOwner = paneFactory.Create();
		var pane = GetBrowsePane(paneOwner);
		var entries = Enumerable
			.Range(0, 10)
			.Select(index => (BrowseLocation)new TagLocation($"tag-{index}"))
			.ToArray();
		var restored = new BrowseNavigationHistorySnapshot(entries, currentIndex: 8);

		await pane.RestoreAsync(restored);

		CollectionAssert.AreEqual(entries[7..10], pane.History.Entries.ToArray());
		Assert.AreEqual(1, pane.History.CurrentIndex);
		Assert.AreEqual(entries[8], pane.Location);
		Assert.IsTrue(pane.CanGoBack);
		Assert.IsTrue(pane.CanGoForward);
	}

	/// <summary>
	/// Test case: equivalent navigation refreshes the stored recovery address.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task EquivalentNavigationRefreshesTheStoredRecoveryAddress()
	{
		var sourceId = new Files.Core.Storage.StorageSourceId("source");
		var before = new FolderLocation(new Files.Core.Storage.StorableReference(sourceId, "item", new Files.Core.Storage.StorageAddress("file", @"C:\before")));
		var after = new FolderLocation(new Files.Core.Storage.StorableReference(sourceId, "item", new Files.Core.Storage.StorageAddress("file", @"C:\after")));
		var resolver = new TestBrowseLocationResolver([]);
		await using var paneOwner = new BrowsePaneSessionFactory(resolver).Create();
		var pane = GetBrowsePane(paneOwner);

		await pane.NavigateAsync(before);
		await pane.NavigateAsync(after);

		Assert.AreEqual(1, pane.History.Entries.Count);
		Assert.AreSame(after, pane.History.Current);
	}

	/// <summary>
	/// Test case: failed equivalent navigation does not rewrite history.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task FailedEquivalentNavigationDoesNotRewriteHistory()
	{
		var sourceId = new Files.Core.Storage.StorageSourceId("source");
		var before = new FolderLocation(new Files.Core.Storage.StorableReference(sourceId, "item", new Files.Core.Storage.StorageAddress("file", @"C:\before")));
		var after = new FolderLocation(new Files.Core.Storage.StorableReference(sourceId, "item", new Files.Core.Storage.StorageAddress("file", @"C:\after")));
		var resolver = new TestBrowseLocationResolver([]);
		await using var paneOwner = new BrowsePaneSessionFactory(resolver).Create();
		var pane = GetBrowsePane(paneOwner);
		await pane.NavigateAsync(before);
		resolver.Exception = new IOException("refresh failed");

		await Assert.ThrowsAsync<IOException>(async () => await pane.NavigateAsync(after));

		Assert.AreSame(before, pane.History.Current);
	}

	/// <summary>
	/// Test case: empty history can restore only an empty pane.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task EmptyHistoryCanRestoreOnlyAnEmptyPane()
	{
		var resolver = new TestBrowseLocationResolver([]);
		await using var paneOwner = new BrowsePaneSessionFactory(resolver).Create();
		var pane = GetBrowsePane(paneOwner);
		var empty = new BrowseNavigationHistorySnapshot(Array.Empty<BrowseLocation>(), currentIndex: -1);

		await pane.RestoreAsync(empty);
		Assert.IsNull(pane.Location);
		Assert.IsEmpty(pane.History.Entries);

		await pane.NavigateAsync(HomeLocation.Instance);
		await Assert.ThrowsAsync<InvalidOperationException>(async () => await pane.RestoreAsync(empty));
	}

	/// <summary>
	/// Test case: application owns windows tabs and split panes.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ApplicationOwnsWindowsTabsAndSplitPanes()
	{
		var resolver = new TestBrowseLocationResolver([]);
		var paneFactory = new BrowsePaneSessionFactory(resolver);
		var application = new FilesApplicationSession(paneFactory);

		var firstWindow = await application.CreateWindowAsync(HomeLocation.Instance);
		var firstTab = firstWindow.ActiveTab!;
		Assert.AreEqual(HomeLocation.Instance, GetBrowsePane(firstTab.ActivePane!).Location);

		var secondTab = await firstWindow.OpenTabAsync(new SearchLocation("query"));
		var secondaryPane = await secondTab.OpenSplitAsync(PaneSplitOrientation.Vertical);

		Assert.AreEqual(2, secondTab.Panes.Count);
		Assert.AreSame(secondaryPane, secondTab.ActivePane);
		Assert.AreEqual(PaneSplitOrientation.Vertical, secondTab.SplitOrientation);
		Assert.AreEqual(new SearchLocation("query"), GetBrowsePane(secondaryPane).Location);
		Assert.IsTrue(secondTab.SetSplitOrientation(PaneSplitOrientation.Horizontal));
		Assert.IsTrue(await secondTab.ClosePaneAsync(secondaryPane.Id));
		Assert.AreEqual(1, secondTab.Panes.Count);
		Assert.AreEqual(PaneSplitOrientation.None, secondTab.SplitOrientation);

		var secondWindow = await application.CreateWindowAsync(new TagLocation("favorites"));
		Assert.AreSame(secondWindow, application.ActiveWindow);
		Assert.IsTrue(application.SetActiveWindow(firstWindow.Id));
		Assert.AreSame(firstWindow, application.ActiveWindow);
		Assert.IsTrue(await application.CloseWindowAsync(secondWindow.Id));
		Assert.AreEqual(1, application.Windows.Count);

		await application.DisposeAsync();

		Assert.IsEmpty(application.Windows);
		Assert.IsTrue(resolver.OpenedContexts.All(static context => context.IsDisposed));
	}

	/// <summary>
	/// Test case: a window owns custom pane content for the lifetime of its tab.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WindowOwnsCustomTabContent()
	{
		var resolver = new TestBrowseLocationResolver([]);
		await using var window = new WindowSession(new BrowsePaneSessionFactory(resolver));
		var content = new TestPaneContentSession();

		var tab = await window.OpenContentTabAsync(() => content);

		Assert.AreSame(content, tab.ActivePane?.Content);
		Assert.IsFalse(content.IsDisposed);
		Assert.IsTrue(await window.CloseTabAsync(tab.Id));
		Assert.IsTrue(content.IsDisposed);
	}

	/// <summary>
	/// Test case: session events do not bubble child state to ancestors.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SessionEventsDoNotBubbleChildStateToAncestors()
	{
		var resolver = new TestBrowseLocationResolver([]);
		await using var application = new FilesApplicationSession(new BrowsePaneSessionFactory(resolver));
		var window = await application.CreateWindowAsync(HomeLocation.Instance);
		var applicationWindowsChanged = 0;
		var applicationActiveWindowChanged = 0;
		var windowTabsChanged = 0;
		var windowActiveTabChanged = 0;
		application.WindowsChanged += (_, _) => applicationWindowsChanged++;
		application.ActiveWindowChanged += (_, _) => applicationActiveWindowChanged++;
		window.TabsChanged += (_, _) => windowTabsChanged++;
		window.ActiveTabChanged += (_, _) => windowActiveTabChanged++;

		var tab = await window.OpenTabAsync(new SearchLocation("events"));

		Assert.AreEqual(0, applicationWindowsChanged);
		Assert.AreEqual(0, applicationActiveWindowChanged);
		Assert.AreEqual(1, windowTabsChanged);
		Assert.AreEqual(1, windowActiveTabChanged);

		var panesChanged = 0;
		var activePaneChanged = 0;
		var splitOrientationChanged = 0;
		tab.PanesChanged += (_, _) => panesChanged++;
		tab.ActivePaneChanged += (_, _) => activePaneChanged++;
		tab.SplitOrientationChanged += (_, _) => splitOrientationChanged++;
		var pane = await tab.OpenSplitAsync(PaneSplitOrientation.Vertical);

		Assert.AreEqual(1, panesChanged);
		Assert.AreEqual(1, activePaneChanged);
		Assert.AreEqual(1, splitOrientationChanged);
		Assert.AreEqual(1, windowTabsChanged);
		Assert.AreEqual(1, windowActiveTabChanged);

		await GetBrowsePane(pane).NavigateAsync(new TagLocation("child-state"));

		Assert.AreEqual(1, panesChanged);
		Assert.AreEqual(1, windowTabsChanged);
		Assert.AreEqual(0, applicationWindowsChanged);
	}

	/// <summary>
	/// Test case: failed window creation disposes the incomplete model graph.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task FailedWindowCreationDisposesTheIncompleteModelGraph()
	{
		var resolver = new TestBrowseLocationResolver([], new InvalidOperationException("open failed"));
		var application = new FilesApplicationSession(new BrowsePaneSessionFactory(resolver));

		await Assert.ThrowsAsync<InvalidOperationException>(async () => await application.CreateWindowAsync(HomeLocation.Instance));

		Assert.IsEmpty(application.Windows);
		Assert.AreEqual(1, resolver.OpenedContexts.Count);
		Assert.IsTrue(resolver.OpenedContexts[0].IsDisposed);
		await application.DisposeAsync();
	}

	private static BrowsePaneSession GetBrowsePane(PaneSession pane)
	{
		return pane.Content as BrowsePaneSession ?? throw new AssertFailedException("Expected browse pane content.");
	}

	private sealed class TestPaneContentSession : IPaneContentSession
	{
		public bool IsDisposed { get; private set; }

		public ValueTask DisposeAsync()
		{
			IsDisposed = true;

			return ValueTask.CompletedTask;
		}
	}
}
