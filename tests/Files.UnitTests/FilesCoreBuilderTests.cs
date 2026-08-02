// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Composition;

namespace Files.UnitTests;

[TestClass]
public sealed class FilesCoreBuilderTests
{
	[TestMethod]
	public async Task RuntimeBuildsNavigableHomeAndOwnsStorageSources()
	{
		var source = new TestStorageSource();
		var runtime = new FilesCoreBuilder()
			.AddStorageSource(source)
			.Build();

		var window = await runtime.Application.CreateWindowAsync(HomeLocation.Instance);

		Assert.AreEqual(HomeLocation.Instance, window.ActiveTab!.ActivePane!.Location);
		Assert.IsEmpty(window.ActiveTab.ActivePane.BrowseSession.Items);
		Assert.AreSame(source, runtime.DataRoot.GetSource(source.SourceId));

		await runtime.DisposeAsync();

		Assert.IsTrue(source.IsDisposed);
		Assert.IsEmpty(runtime.Application.Windows);
	}

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

	[TestMethod]
	public void BuilderRejectsDuplicateSourceIds()
	{
		var builder = new FilesCoreBuilder()
			.AddStorageSource(new TestStorageSource());

		Assert.Throws<InvalidOperationException>(() => builder.AddStorageSource(new TestStorageSource()));
	}

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
