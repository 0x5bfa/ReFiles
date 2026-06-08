// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.AppModels;
using Files.Core.Browsing;

namespace Files.UnitTests;

[TestClass]
public sealed class AppModelTests
{
	[TestMethod]
	public async Task PaneNavigationCommitsHistoryAndDropsForwardBranch()
	{
		var resolver = new TestBrowseLocationResolver([]);
		var paneFactory = new BrowsePaneFactory(
			resolver,
			historyCapacity: 4);
		await using var pane = paneFactory.Create();
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

		CollectionAssert.AreEqual(
			new BrowseLocation[] { home, search, branch },
			pane.History.Entries.ToArray());
		Assert.AreEqual(branch, pane.History.Current);
		Assert.IsFalse(pane.CanGoForward);
	}

	[TestMethod]
	public async Task PaneRestoresBoundedHistoryAroundCurrentEntry()
	{
		var resolver = new TestBrowseLocationResolver([]);
		var paneFactory = new BrowsePaneFactory(
			resolver,
			historyCapacity: 3);
		await using var pane = paneFactory.Create();
		var entries = Enumerable
			.Range(0, 10)
			.Select(index => (BrowseLocation)new TagLocation($"tag-{index}"))
			.ToArray();
		var restored = new BrowseNavigationHistorySnapshot(
			entries,
			currentIndex: 8);

		await pane.RestoreAsync(restored);

		CollectionAssert.AreEqual(
			entries[7..10],
			pane.History.Entries.ToArray());
		Assert.AreEqual(1, pane.History.CurrentIndex);
		Assert.AreEqual(entries[8], pane.Location);
		Assert.IsTrue(pane.CanGoBack);
		Assert.IsTrue(pane.CanGoForward);
	}

	[TestMethod]
	public async Task EquivalentNavigationRefreshesTheStoredRecoveryAddress()
	{
		var sourceId = new Files.Core.Storage.StorageSourceId("source");
		var before = new FolderLocation(
			new Files.Core.Storage.StorableReference(
				sourceId,
				"item",
				new Files.Core.Storage.StorageAddress(
					"file",
					@"C:\before")));
		var after = new FolderLocation(
			new Files.Core.Storage.StorableReference(
				sourceId,
				"item",
				new Files.Core.Storage.StorageAddress(
					"file",
					@"C:\after")));
		var resolver = new TestBrowseLocationResolver([]);
		await using var pane = new BrowsePaneFactory(resolver).Create();

		await pane.NavigateAsync(before);
		await pane.NavigateAsync(after);

		Assert.AreEqual(1, pane.History.Entries.Count);
		Assert.AreSame(after, pane.History.Current);
	}

	[TestMethod]
	public async Task FailedEquivalentNavigationDoesNotRewriteHistory()
	{
		var sourceId = new Files.Core.Storage.StorageSourceId("source");
		var before = new FolderLocation(
			new Files.Core.Storage.StorableReference(
				sourceId,
				"item",
				new Files.Core.Storage.StorageAddress(
					"file",
					@"C:\before")));
		var after = new FolderLocation(
			new Files.Core.Storage.StorableReference(
				sourceId,
				"item",
				new Files.Core.Storage.StorageAddress(
					"file",
					@"C:\after")));
		var resolver = new TestBrowseLocationResolver([]);
		await using var pane = new BrowsePaneFactory(resolver).Create();
		await pane.NavigateAsync(before);
		resolver.Exception = new IOException("refresh failed");

		await Assert.ThrowsAsync<IOException>(
			async () => await pane.NavigateAsync(after));

		Assert.AreSame(before, pane.History.Current);
	}

	[TestMethod]
	public async Task EmptyHistoryCanRestoreOnlyAnEmptyPane()
	{
		var resolver = new TestBrowseLocationResolver([]);
		await using var pane = new BrowsePaneFactory(resolver).Create();
		var empty = new BrowseNavigationHistorySnapshot(
			Array.Empty<BrowseLocation>(),
			currentIndex: -1);

		await pane.RestoreAsync(empty);
		Assert.IsNull(pane.Location);
		Assert.IsEmpty(pane.History.Entries);

		await pane.NavigateAsync(HomeLocation.Instance);
		await Assert.ThrowsAsync<InvalidOperationException>(
			async () => await pane.RestoreAsync(empty));
	}

	[TestMethod]
	public async Task ApplicationOwnsWindowsTabsAndSplitPanes()
	{
		var resolver = new TestBrowseLocationResolver([]);
		var paneFactory = new BrowsePaneFactory(resolver);
		var application = new FilesApplicationModel(paneFactory);

		var firstWindow = await application.CreateWindowAsync(
			HomeLocation.Instance);
		var firstTab = firstWindow.ActiveTab!;
		Assert.AreEqual(HomeLocation.Instance, firstTab.ActivePane!.Location);

		var secondTab = await firstWindow.OpenTabAsync(
			new SearchLocation("query"));
		var secondaryPane = await secondTab.OpenSplitAsync(
			PaneSplitOrientation.Vertical);

		Assert.AreEqual(2, secondTab.Panes.Count);
		Assert.AreSame(secondaryPane, secondTab.ActivePane);
		Assert.AreEqual(
			PaneSplitOrientation.Vertical,
			secondTab.SplitOrientation);
		Assert.AreEqual(new SearchLocation("query"), secondaryPane.Location);
		Assert.IsTrue(
			secondTab.SetSplitOrientation(
				PaneSplitOrientation.Horizontal));
		Assert.IsTrue(await secondTab.ClosePaneAsync(secondaryPane.Id));
		Assert.AreEqual(1, secondTab.Panes.Count);
		Assert.AreEqual(PaneSplitOrientation.None, secondTab.SplitOrientation);

		var secondWindow = await application.CreateWindowAsync(
			new TagLocation("favorites"));
		Assert.AreSame(secondWindow, application.ActiveWindow);
		Assert.IsTrue(application.SetActiveWindow(firstWindow.Id));
		Assert.AreSame(firstWindow, application.ActiveWindow);
		Assert.IsTrue(await application.CloseWindowAsync(secondWindow.Id));
		Assert.AreEqual(1, application.Windows.Count);

		await application.DisposeAsync();

		Assert.IsEmpty(application.Windows);
		Assert.IsTrue(
			resolver.OpenedContexts.All(static context => context.IsDisposed));
	}

	[TestMethod]
	public async Task FailedWindowCreationDisposesTheIncompleteModelGraph()
	{
		var resolver = new TestBrowseLocationResolver(
			[],
			new InvalidOperationException("open failed"));
		var application = new FilesApplicationModel(
			new BrowsePaneFactory(resolver));

		await Assert.ThrowsAsync<InvalidOperationException>(
			async () => await application.CreateWindowAsync(
				HomeLocation.Instance));

		Assert.IsEmpty(application.Windows);
		Assert.AreEqual(1, resolver.OpenedContexts.Count);
		Assert.IsTrue(resolver.OpenedContexts[0].IsDisposed);
		await application.DisposeAsync();
	}
}
