// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.Browsing;
using Files.Core.Models;

namespace Files.UnitTests;

[TestClass]
public sealed class StorableModelLifetimeTests
{
	[TestMethod]
	public async Task ModelAwaitsFeaturesBeforeAsyncCoreModel()
	{
		var order = new List<string>();
		var featureRegistry = new ItemFeatureBuilder()
			.Add<AsyncOrderFeature>(
				new DelegateItemFeatureFactory<AsyncOrderFeature>(
					_ => new AsyncOrderFeature(order)))
			.Build();
		var factory = new StorableModelFactory(featureRegistry);
		var coreModel = new AsyncOrderStorable("item", "Item", order);
		var model = factory.Create(new TestStorageSource(), coreModel);

		Assert.IsNotNull(model.Get<AsyncOrderFeature>());
		await model.DisposeAsync();

		CollectionAssert.AreEqual(
			new[] { "feature", "core" },
			order);
	}

	[TestMethod]
	public async Task BrowseSessionAwaitsItemDisposalDuringReplacementAndShutdown()
	{
		var firstCore = new AsyncOrderStorable(
			"first",
			"First",
			[]);
		var secondCore = new AsyncOrderStorable(
			"second",
			"Second",
			[]);
		var source = new TestStorageSource();
		var factory = new StorableModelFactory();
		var firstModel = factory.Create(source, firstCore);
		var secondModel = factory.Create(source, secondCore);
		var resolver = new TestBrowseLocationResolver([firstModel]);
		var session = new BrowseSessionModel(resolver);

		try
		{
			await session.NavigateAsync(HomeLocation.Instance);
			resolver.Items.Clear();
			resolver.Items.Add(secondModel);

			await session.NavigateAsync(new TagLocation("next"));

			Assert.IsTrue(firstCore.IsDisposed);
			Assert.IsFalse(secondCore.IsDisposed);
		}
		finally
		{
			await session.DisposeAsync();
			await source.DisposeAsync();
		}

		Assert.IsTrue(secondCore.IsDisposed);
	}

	[TestMethod]
	public void FailedConstructionDisposesAsyncCoreModel()
	{
		var coreModel = new AsyncOrderStorable(
			string.Empty,
			"Invalid",
			[]);
		var factory = new StorableModelFactory();

		Assert.Throws<ArgumentException>(
			() => factory.Create(new TestStorageSource(), coreModel));

		Assert.IsTrue(coreModel.IsDisposed);
	}

	private sealed class AsyncOrderFeature : IAsyncDisposable
	{
		private readonly IList<string> order;

		public AsyncOrderFeature(IList<string> order)
		{
			this.order = order;
		}

		public async ValueTask DisposeAsync()
		{
			await Task.Yield();
			order.Add("feature");
		}
	}

	private sealed class AsyncOrderStorable : IStorable, IAsyncDisposable
	{
		private readonly IList<string> order;

		public AsyncOrderStorable(
			string id,
			string name,
			IList<string> order)
		{
			Id = id;
			Name = name;
			this.order = order;
		}

		public string Id { get; }

		public string Name { get; }

		public bool IsDisposed { get; private set; }

		public async ValueTask DisposeAsync()
		{
			await Task.Yield();
			IsDisposed = true;
			order.Add("core");
		}
	}
}
