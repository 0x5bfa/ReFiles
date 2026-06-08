// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UnitTests;

[TestClass]
public sealed class ItemFeatureRegistryTests
{
	[TestMethod]
	public void GetIsLazyAndCached()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(factory.Source.SourceId, coreModel.Id);
		var context = new ItemContext(factory.Source, coreModel, reference);
		var createCount = 0;
		var feature = new TestItemFeature("feature", []);
		var featureRegistry = new ItemFeatureBuilder()
			.Add<TestItemFeature>(
				new DelegateItemFeatureFactory<TestItemFeature>(_ =>
				{
					createCount++;
					return feature;
				}))
			.Build();

		using var features = featureRegistry.CreateFeatures(context);
		Assert.AreEqual(0, createCount);

		Assert.AreSame(feature, features.Get<TestItemFeature>());
		Assert.AreSame(feature, features.Get<TestItemFeature>());
		Assert.AreEqual(1, createCount);
	}

	[TestMethod]
	public void ItemOwnedFeaturesAreDisposedInReverseCreationOrder()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(factory.Source.SourceId, coreModel.Id);
		var context = new ItemContext(factory.Source, coreModel, reference);
		var disposalOrder = new List<string>();
		var innerFeature = new TestItemFeature("inner", disposalOrder);
		var wrapper = new TestItemFeature("wrapper", disposalOrder);
		var featureRegistry = new ItemFeatureBuilder()
			.Add<TestItemFeature>(
				new DelegateItemFeatureFactory<TestItemFeature>(_ => innerFeature))
			.AddWrapper<TestItemFeature>(
				new DelegateItemFeatureWrapper<TestItemFeature>((_, _) => wrapper))
			.Build();

		using (var features = featureRegistry.CreateFeatures(context))
		{
			Assert.AreSame(wrapper, features.Get<TestItemFeature>());
		}

		CollectionAssert.AreEqual(new[] { "wrapper", "inner" }, disposalOrder);
	}

	[TestMethod]
	public void SharedFeaturesAreNotDisposedByTheItem()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(factory.Source.SourceId, coreModel.Id);
		var context = new ItemContext(factory.Source, coreModel, reference);
		var feature = new TestItemFeature("shared", []);
		var featureRegistry = new ItemFeatureBuilder()
			.Add<TestItemFeature>(
				new DelegateItemFeatureFactory<TestItemFeature>(_ => feature),
				lifetime: ItemFeatureLifetime.Shared)
			.Build();

		using (var features = featureRegistry.CreateFeatures(context))
		{
			Assert.AreSame(feature, features.Get<TestItemFeature>());
		}

		Assert.IsFalse(feature.IsDisposed);
	}

	[TestMethod]
	public async Task AsyncFeaturesAreAwaitedDuringModelDisposal()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(
			factory.Source.SourceId,
			coreModel.Id);
		var context = new ItemContext(
			factory.Source,
			coreModel,
			reference);
		var feature = new AsyncTestFeature();
		var featureRegistry = new ItemFeatureBuilder()
			.Add<AsyncTestFeature>(
				new DelegateItemFeatureFactory<AsyncTestFeature>(
					_ => feature))
			.Build();
		var features = featureRegistry.CreateFeatures(context);

		Assert.AreSame(feature, features.Get<AsyncTestFeature>());
		await features.DisposeAsync();

		Assert.IsTrue(feature.IsDisposed);
		Assert.AreEqual(1, feature.DisposeCount);
	}

	[TestMethod]
	public void ResolutionAndCleanupFailuresAreBothPreserved()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(
			factory.Source.SourceId,
			coreModel.Id);
		var context = new ItemContext(
			factory.Source,
			coreModel,
			reference);
		var feature = new ThrowingDisposableFeature();
		var featureRegistry = new ItemFeatureBuilder()
			.Add<ThrowingDisposableFeature>(
				new DelegateItemFeatureFactory<ThrowingDisposableFeature>(
					_ => feature))
			.AddWrapper<ThrowingDisposableFeature>(
				new DelegateItemFeatureWrapper<ThrowingDisposableFeature>(
					(_, _) => throw new InvalidOperationException(
						"resolution failed")))
			.Build();
		using var features = featureRegistry.CreateFeatures(context);

		var error = Assert.Throws<AggregateException>(
			() => features.Get<ThrowingDisposableFeature>());

		Assert.AreEqual(2, error.InnerExceptions.Count);
		Assert.IsTrue(
			error.InnerExceptions.Any(
				static exception =>
					exception.Message == "resolution failed"));
		Assert.IsTrue(
			error.InnerExceptions.Any(
				static exception =>
					exception.Message == "cleanup failed"));
		Assert.AreEqual(1, feature.DisposeCount);
	}

	[TestMethod]
	public void MultipleOptionsWithoutACombinerFailExplicitly()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(factory.Source.SourceId, coreModel.Id);
		var context = new ItemContext(factory.Source, coreModel, reference);
		var featureRegistry = new ItemFeatureBuilder()
			.Add<TestItemFeature>(new DelegateItemFeatureFactory<TestItemFeature>(_ => new TestItemFeature("one", [])))
			.Add<TestItemFeature>(new DelegateItemFeatureFactory<TestItemFeature>(_ => new TestItemFeature("two", [])))
			.Build();

		using var features = featureRegistry.CreateFeatures(context);
		Assert.Throws<InvalidOperationException>(() => features.Get<TestItemFeature>());
	}

	[TestMethod]
	public void PriorityCombinerRejectsTiesAtTheHighestPriority()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(factory.Source.SourceId, coreModel.Id);
		var context = new ItemContext(factory.Source, coreModel, reference);
		var featureRegistry = new ItemFeatureBuilder()
			.Add<TestItemFeature>(new DelegateItemFeatureFactory<TestItemFeature>(_ => new TestItemFeature("one", [])), priority: 10)
			.Add<TestItemFeature>(new DelegateItemFeatureFactory<TestItemFeature>(_ => new TestItemFeature("two", [])), priority: 10)
			.SetCombiner<TestItemFeature>(new PriorityItemFeatureCombiner<TestItemFeature>())
			.Build();

		using var features = featureRegistry.CreateFeatures(context);
		Assert.Throws<InvalidOperationException>(() => features.Get<TestItemFeature>());
	}

	private sealed class AsyncTestFeature : IAsyncDisposable
	{
		public bool IsDisposed { get; private set; }

		public int DisposeCount { get; private set; }

		public async ValueTask DisposeAsync()
		{
			await Task.Yield();
			DisposeCount++;
			IsDisposed = true;
		}
	}

	private sealed class ThrowingDisposableFeature : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose()
		{
			DisposeCount++;
			throw new InvalidOperationException("cleanup failed");
		}
	}
}
