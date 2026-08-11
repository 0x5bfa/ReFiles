// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Composition;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Sessions;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for files core builder behavior.
/// </summary>
[TestClass]
public sealed class FilesCoreBuilderTests
{
	/// <summary>
	/// Test case: runtime builds navigable home and owns storage sources.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task RuntimeBuildsNavigableHomeAndOwnsStorageSources()
	{
		var source = new TestStorageSource();
		var runtime = new FilesCoreBuilder()
			.AddStorageSource(source)
			.Build();

		var window = await runtime.ShellSession.CreateWindowAsync(HomeLocation.Instance);
		var pane = window.ActiveTab!.ActivePane!;
		var browsePane = pane.Content as BrowsePaneSession;

		Assert.IsNotNull(browsePane);
		Assert.AreEqual(HomeLocation.Instance, browsePane.Location);
		Assert.IsEmpty(browsePane.BrowseSession.Items);
		Assert.AreSame(source, runtime.Workspace.Sources.Single());

		await runtime.DisposeAsync();

		Assert.IsTrue(source.IsDisposed);
		Assert.IsEmpty(runtime.ShellSession.Windows);
	}

	/// <summary>
	/// Test case: storage workspace can be used without creating window.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task StorageWorkspaceCanBeUsedWithoutCreatingWindow()
	{
		var source = new TestStorageSource();
		var runtime = new FilesCoreBuilder()
			.AddStorageSource(source)
			.Build();
		IStorageWorkspace workspace = runtime.Workspace;
		var roots = new List<IFolderModel>();

		await foreach (var root in workspace.GetRootsAsync(source.SourceId))
		{
			roots.Add(root);
		}

		Assert.IsEmpty(roots);
		Assert.IsEmpty(runtime.ShellSession.Windows);
		Assert.AreSame(workspace, runtime.Workspace);

		await runtime.DisposeAsync();

		Assert.IsTrue(source.IsDisposed);
	}

	/// <summary>
	/// Test case: unbuilt builder disposes accepted resources.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task UnbuiltBuilderDisposesAcceptedResources()
	{
		var source = new TestStorageSource();
		var builder = new FilesCoreBuilder()
			.AddStorageSource(source);

		await builder.DisposeAsync();
		await builder.DisposeAsync();

		Assert.IsTrue(source.IsDisposed);
		Assert.AreEqual(1, source.DisposeCount);
		Assert.Throws<ObjectDisposedException>(() => builder.AddStorageSource(new TestStorageSource()));
	}

	/// <summary>
	/// Test case: built builder transfers resources to runtime.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task BuiltBuilderTransfersResourcesToRuntime()
	{
		var source = new TestStorageSource();
		var builder = new FilesCoreBuilder()
			.AddStorageSource(source);
		var runtime = builder.Build();

		await builder.DisposeAsync();

		Assert.IsFalse(source.IsDisposed);
		await runtime.DisposeAsync();
		Assert.IsTrue(source.IsDisposed);
	}

	/// <summary>
	/// Test case: builder rejects duplicate source ids.
	/// </summary>
	[TestMethod]
	public void BuilderRejectsDuplicateSourceIds()
	{
		var builder = new FilesCoreBuilder()
			.AddStorageSource(new TestStorageSource());

		Assert.Throws<InvalidOperationException>(() => builder.AddStorageSource(new TestStorageSource()));
	}

	/// <summary>
	/// Test case: runtime disposal is idempotent.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task RuntimeDisposalIsIdempotent()
	{
		var source = new TestStorageSource();
		var runtime = new FilesCoreBuilder()
			.AddStorageSource(source)
			.Build();

		await runtime.DisposeAsync();
		await runtime.DisposeAsync();

		Assert.AreEqual(1, source.DisposeCount);
	}

	/// <summary>
	/// Test case: failed build disposes registered sources.
	/// </summary>
	[TestMethod]
	public void FailedBuildDisposesRegisteredSources()
	{
		var source = new TestStorageSource();
		var builder = new FilesCoreBuilder()
			.AddStorageSource(source)
			.AddBrowseLocationHandler(_ => throw new InvalidOperationException("factory failed"));

		Assert.Throws<InvalidOperationException>(() => builder.Build());

		Assert.IsTrue(source.IsDisposed);
		Assert.AreEqual(1, source.DisposeCount);
	}
}
