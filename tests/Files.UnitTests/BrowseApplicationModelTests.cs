// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Sessions;
using Files.Core.Storage;
using Files.Core.Storage.Ftp;

namespace Files.UnitTests;

/// <summary>Protects headless application behavior and content ownership.</summary>
[TestClass]
public sealed class BrowseApplicationModelTests
{
	/// <summary>Search routing and history work without a Windows source or presentation.</summary>
	[TestMethod]
	public async Task SearchUsesConfiguredProviderAndRestoresItsOrigin()
	{
		await using var workspace = new StorageWorkspace([], new StorableModelFactory());
		var services = new BrowseApplicationServices(workspace, new RecordingOperations(), new BrowseSearchService([new SearchProvider()]));
		await using var owner = new BrowsePaneSessionFactory(new TestBrowseLocationResolver([]), services: services).Create();
		var pane = (BrowsePaneSession)owner.Content;
		var origin = new FolderLocation(new StorableReference(new StorageSourceId("remote"), "/docs"));
		await pane.NavigateAsync(origin);

		Assert.IsTrue(pane.CanSearch);
		await pane.SearchAsync("first");
		await pane.SearchAsync("second");

		Assert.AreEqual(new SearchLocation("second", origin.Folder, origin), pane.Location);
		Assert.AreEqual(2, pane.History.Entries.Count);
		await pane.SearchAsync(string.Empty);

		Assert.AreEqual(origin, pane.Location);
		Assert.IsTrue(pane.CanGoForward);
	}

	/// <summary>A failed search does not commit a new location or history entry.</summary>
	[TestMethod]
	public async Task SearchFailurePreservesTheCommittedState()
	{
		await using var workspace = new StorageWorkspace([], new StorableModelFactory());
		var resolver = new TestBrowseLocationResolver([]);
		var services = new BrowseApplicationServices(workspace, new RecordingOperations(), new SearchProvider());
		await using var owner = new BrowsePaneSessionFactory(resolver, services: services).Create();
		var pane = (BrowsePaneSession)owner.Content;
		await pane.NavigateAsync(HomeLocation.Instance);
		resolver.Exception = new IOException("search unavailable");

		await Assert.ThrowsAsync<IOException>(async () => await pane.SearchAsync("query"));

		Assert.AreEqual(HomeLocation.Instance, pane.Location);
		Assert.AreEqual(1, pane.History.Entries.Count);
	}

	/// <summary>A selection is submitted as one complete batch to the configured executor.</summary>
	[TestMethod]
	public async Task DeleteSelectionUsesOneProviderIndependentBatch()
	{
		var factory = new TestModelFactory();
		var first = factory.CreateModel("one", "One", out _);
		var second = factory.CreateModel("two", "Two", out _);
		await using var workspace = new StorageWorkspace([factory.Source], new StorableModelFactory());
		var operations = new RecordingOperations();
		var services = new BrowseApplicationServices(workspace, operations);
		await using var owner = new BrowsePaneSessionFactory(new TestBrowseLocationResolver([first, second]), services: services).Create();
		var pane = (BrowsePaneSession)owner.Content;
		await pane.NavigateAsync(HomeLocation.Instance);
		pane.BrowseSession.SetSelection([first.Reference.GetKey(), second.Reference.GetKey()], null, null);

		Assert.IsTrue(pane.CanDeleteSelection);
		var result = await pane.DeleteSelectionAsync();

		Assert.IsTrue(result.Succeeded);
		Assert.AreEqual(1, operations.BatchCount);
		Assert.AreEqual(2, operations.Requests.Count);
		Assert.IsTrue(operations.Requests.Cast<DeleteOperationRequest>().All(static request => request.Permanently));
	}

	/// <summary>Changing the live selection does not retarget a captured deletion.</summary>
	/// <returns>The asynchronous regression test.</returns>
	[TestMethod]
	public async Task DeleteItemsKeepsTheCapturedTargets()
	{
		var factory = new TestModelFactory();
		var first = factory.CreateModel("one", "One", out _);
		var second = factory.CreateModel("two", "Two", out _);
		await using var workspace = new StorageWorkspace([factory.Source], new StorableModelFactory());
		var operations = new RecordingOperations();
		var services = new BrowseApplicationServices(workspace, operations);
		await using var owner = new BrowsePaneSessionFactory(new TestBrowseLocationResolver([first, second]), services: services).Create();
		var pane = (BrowsePaneSession)owner.Content;
		await pane.NavigateAsync(HomeLocation.Instance);
		pane.BrowseSession.SetSelection([second.Reference.GetKey()], null, null);

		await pane.DeleteItemsAsync([first.Reference]);

		Assert.AreEqual(first.Reference, ((DeleteOperationRequest)operations.Requests.Single()).Item);
	}

	/// <summary>Transfers resolve captured addresses and submit one provider-independent batch.</summary>
	/// <param name="move">Whether the batch contains move requests.</param>
	/// <returns>The asynchronous regression test.</returns>
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task TransferAddressesSubmitsOneCapturedBatch(bool move)
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFolder("/destination");
		sessions.AddFile("/one.txt", [1]);
		sessions.AddFile("/two.txt", [2]);
		var operations = new RecordingOperations();
		var profile = new FtpConnectionProfile("transfer", "Transfer", "example.test");
		await using var runtime = new FilesCoreBuilder().AddFtpStorage(profile, sessionFactory: sessions, enableArchives: false, enableOperations: false).UseStorageOperations(operations).Build();
		var source = (FtpStorageSource)runtime.Workspace.Sources.Single();
		var window = await runtime.ShellSession.CreateWindowAsync();
		var pane = (BrowsePaneSession)window.ActiveTab!.ActivePane!.Content;
		var destination = source.CreateReference(FtpPath.Parse("/destination"));
		var addresses = new[] { source.CreateAddress(FtpPath.Parse("/one.txt")), source.CreateAddress(FtpPath.Parse("/two.txt")) };

		await pane.TransferAddressesAsync(addresses, destination, move);

		Assert.AreEqual(1, operations.BatchCount);
		Assert.AreEqual(2, operations.Requests.Count);
		if (move)
		{
			Assert.IsTrue(operations.Requests.Cast<MoveOperationRequest>().All(request => request.DestinationFolder == destination));
		}
		else
		{
			Assert.IsTrue(operations.Requests.Cast<CopyOperationRequest>().All(request => request.DestinationFolder == destination));
		}
	}

	/// <summary>The runtime exposes the same operation implementation passed to its model graph.</summary>
	[TestMethod]
	public async Task RuntimeUsesTheInjectedOperationBoundary()
	{
		var operations = new RecordingOperations();
		await using var runtime = new FilesCoreBuilder().UseStorageOperations(operations).Build();

		Assert.AreSame(operations, runtime.StorageOperations);
		var window = await runtime.ShellSession.CreateWindowAsync(HomeLocation.Instance);

		Assert.IsNotNull(window.ActiveTab?.ActivePane);
	}

	/// <summary>Generic content can initialize and split without any BrowseSession.</summary>
	[TestMethod]
	public async Task CustomContentOwnsItsInitializationAndSplitPolicy()
	{
		await using var window = new WindowSession(new BrowsePaneSessionFactory(new TestBrowseLocationResolver([])));
		var original = new CustomContent();
		var tab = await window.OpenContentTabAsync(() => original);

		Assert.IsTrue(original.Initialized);
		Assert.IsTrue(tab.CanSplit);
		var split = await tab.OpenSplitAsync(PaneSplitOrientation.Horizontal);
		var child = (CustomContent)split.Content;

		Assert.AreNotSame(original, child);
		Assert.IsTrue(child.Initialized);
		Assert.IsTrue(await window.CloseTabAsync(tab.Id));
		Assert.IsTrue(original.Disposed);
		Assert.IsTrue(child.Disposed);
	}

	/// <summary>Content without a split policy is rejected before the tab changes.</summary>
	[TestMethod]
	public async Task SettingsContentCannotAccidentallyCreateABrowseSplit()
	{
		await using var window = new WindowSession(new BrowsePaneSessionFactory(new TestBrowseLocationResolver([])));
		var tab = await window.OpenContentTabAsync(static () => new SettingsPaneSession());

		Assert.IsFalse(tab.CanSplit);
		await Assert.ThrowsAsync<NotSupportedException>(async () => await tab.OpenSplitAsync(PaneSplitOrientation.Vertical));

		Assert.AreEqual(1, tab.Panes.Count);
		Assert.AreEqual(PaneSplitOrientation.None, tab.SplitOrientation);
	}

	/// <summary>A captured browse factory remains valid after the original tab is disposed.</summary>
	[TestMethod]
	public async Task RestoredContentHasIndependentOwnership()
	{
		await using var window = new WindowSession(new BrowsePaneSessionFactory(new TestBrowseLocationResolver([])));
		var location = new TagLocation("saved");
		var original = await window.OpenTabAsync(location);
		var factory = ((IRestorablePaneContent)original.ActivePane!.Content).CaptureRestoreFactory();
		await window.CloseTabAsync(original.Id);
		var restored = await window.OpenContentTabAsync(factory);

		Assert.AreEqual(location, ((BrowsePaneSession)restored.ActivePane!.Content).Location);
		Assert.AreNotSame(original.ActivePane, restored.ActivePane);
	}

	/// <summary>Hidden metadata is available from non-Windows storage implementations.</summary>
	[TestMethod]
	public async Task ItemAttributesDoNotRequireAWindowsProvider()
	{
		await using var source = new TestStorageSource();
		await using var model = new StorableModelFactory().Create(source, new HiddenItem());

		Assert.IsTrue(model.IsHidden);
	}

	/// <summary>Column defaults and sorting support are provider-independent.</summary>
	[TestMethod]
	public void ColumnCapabilitiesPreserveProviderSemantics()
	{
		var columns = new BrowseColumnSet([new BrowseColumn(0, "custom", "Custom", canSort: false, canGroup: false)], 0);
		var capabilities = Files.Core.ViewSettings.BrowseViewCapabilities.FromColumns(columns);

		Assert.IsTrue(capabilities.IsColumnAvailable("custom"));
		Assert.IsFalse(capabilities.CanSortBy("custom"));
		Assert.IsFalse(capabilities.CanGroupBy("custom"));
	}

	/// <summary>Hierarchy traversal and child navigation use configured storage without any view or Shell source.</summary>
	[TestMethod]
	public async Task NavigationHierarchyWorksWithAnInMemoryFtpProvider()
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFolder("/docs");
		sessions.AddFolder("/docs/sub");
		sessions.AddFile("/docs/readme.txt", [1]);
		var profile = new FtpConnectionProfile("navigation", "Navigation FTP", "example.test");
		await using var runtime = new FilesCoreBuilder().AddFtpStorage(profile, sessionFactory: sessions, enableArchives: false).Build();
		var source = (FtpStorageSource)runtime.Workspace.Sources.Single();
		var location = new FolderLocation(source.CreateReference(FtpPath.Parse("/docs/sub")));
		var window = await runtime.ShellSession.CreateWindowAsync(location);
		var pane = (BrowsePaneSession)window.ActiveTab!.ActivePane!.Content;

		var hierarchy = await pane.Navigation!.GetBreadcrumbsAsync();
		var children = await pane.Navigation.GetChildrenAsync(new FolderLocation(source.CreateReference(FtpPath.Parse("/docs"))));

		Assert.AreEqual(location, hierarchy[^1].Location);
		Assert.IsTrue(hierarchy.Count >= 2);
		Assert.AreEqual(1, children.Count);
		Assert.AreEqual("sub", children[0].Text);
	}

	private sealed class SearchProvider : IBrowseSearchProvider
	{
		public bool CanSearch(BrowseLocation location)
		{
			return location is HomeLocation or FolderLocation or SearchLocation;
		}

		public BrowseLocation CreateSearch(string query, BrowseLocation location)
		{
			var origin = location is SearchLocation current ? current.Origin : location;

			return new SearchLocation(query, (origin as FolderLocation)?.Folder, origin);
		}
	}

	private sealed class RecordingOperations : IStorageOperationService
	{
		public int BatchCount { get; private set; }
		public IReadOnlyList<StorageOperationRequest> Requests { get; private set; } = [];

		public bool CanHandle(StorageOperationRequest request)
		{
			return true;
		}

		public ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null,
			CancellationToken cancellationToken = default, IStorageOperationControl? operationControl = null)
		{
			throw new InvalidOperationException("The application must submit the entire batch.");
		}

		public ValueTask<StorageOperationResult> ExecuteBatchAsync(IReadOnlyList<StorageOperationRequest> requests, CancellationToken cancellationToken = default)
		{
			BatchCount++;
			Requests = requests.ToArray();

			return ValueTask.FromResult(new StorageOperationResult(true, null));
		}
	}

	private sealed class CustomContent : IPaneContentSession, ISplittablePaneContent
	{
		public bool Initialized { get; private set; }
		public bool Disposed { get; private set; }

		public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
		{
			Initialized = true;

			return ValueTask.CompletedTask;
		}

		public async ValueTask<IPaneContentSession> CreateSplitAsync(CancellationToken cancellationToken = default)
		{
			var child = new CustomContent();
			await child.InitializeAsync(cancellationToken);

			return child;
		}

		public ValueTask DisposeAsync()
		{
			Disposed = true;

			return ValueTask.CompletedTask;
		}
	}

	private sealed class HiddenItem : IStorable, IStorageItemAttributes
	{
		public string Id => "hidden";
		public string Name => "Hidden";
		public bool IsHidden => true;
	}
}
