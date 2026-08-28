// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for item capability registry behavior.
/// </summary>
[TestClass]
public sealed class CapabilityRegistryTests
{
	/// <summary>
	/// Test case: get is lazy and cached.
	/// </summary>
	[TestMethod]
	public void GetIsLazyAndCached()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(factory.Source.SourceId, coreModel.Id);
		var context = new ItemContext(factory.Source, coreModel, reference);
		var createCount = 0;
		var capability = new TestCapability("capability", []);
		var capabilityRegistry = new CapabilityBuilder()
			.Add<TestCapability>(new DelegateCapabilityFactory<TestCapability>(_ => {createCount++; return capability;}))
			.Build();

		using var capabilities = capabilityRegistry.CreateCapabilities(context);
		Assert.AreEqual(0, createCount);

		Assert.AreSame(capability, capabilities.Get<TestCapability>());
		Assert.AreSame(capability, capabilities.Get<TestCapability>());
		Assert.AreEqual(1, createCount);
	}

	/// <summary>
	/// Test case: item owned capabilities are disposed in reverse creation order.
	/// </summary>
	[TestMethod]
	public void ItemOwnedCapabilitiesAreDisposedInReverseCreationOrder()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(factory.Source.SourceId, coreModel.Id);
		var context = new ItemContext(factory.Source, coreModel, reference);
		var disposalOrder = new List<string>();
		var innerCapability = new TestCapability("inner", disposalOrder);
		var wrapper = new TestCapability("wrapper", disposalOrder);
		var capabilityRegistry = new CapabilityBuilder()
			.Add<TestCapability>(new DelegateCapabilityFactory<TestCapability>(_ => innerCapability))
			.AddWrapper<TestCapability>(new DelegateCapabilityWrapper<TestCapability>((_, _) => wrapper))
			.Build();

		using (var capabilities = capabilityRegistry.CreateCapabilities(context))
		{
			Assert.AreSame(wrapper, capabilities.Get<TestCapability>());
		}

		CollectionAssert.AreEqual(new[] { "wrapper", "inner" }, disposalOrder);
	}

	/// <summary>
	/// Test case: shared capabilities are not disposed by the item.
	/// </summary>
	[TestMethod]
	public void SharedCapabilitiesAreNotDisposedByTheItem()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(factory.Source.SourceId, coreModel.Id);
		var context = new ItemContext(factory.Source, coreModel, reference);
		var capability = new TestCapability("shared", []);
		var capabilityRegistry = new CapabilityBuilder()
			.Add<TestCapability>(new DelegateCapabilityFactory<TestCapability>(_ => capability), lifetime: CapabilityLifetime.Shared)
			.Build();

		using (var capabilities = capabilityRegistry.CreateCapabilities(context))
		{
			Assert.AreSame(capability, capabilities.Get<TestCapability>());
		}

		Assert.IsFalse(capability.IsDisposed);
	}

	/// <summary>
	/// Test case: async capabilities are awaited during model disposal.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task AsyncCapabilitiesAreAwaitedDuringModelDisposal()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(factory.Source.SourceId, coreModel.Id);
		var context = new ItemContext(factory.Source, coreModel, reference);
		var capability = new AsyncTestCapability();
		var capabilityRegistry = new CapabilityBuilder()
			.Add<AsyncTestCapability>(new DelegateCapabilityFactory<AsyncTestCapability>(_ => capability))
			.Build();
		var capabilities = capabilityRegistry.CreateCapabilities(context);

		Assert.AreSame(capability, capabilities.Get<AsyncTestCapability>());
		await capabilities.DisposeAsync();

		Assert.IsTrue(capability.IsDisposed);
		Assert.AreEqual(1, capability.DisposeCount);
	}

	/// <summary>
	/// Test case: resolution and cleanup failures are both preserved.
	/// </summary>
	[TestMethod]
	public void ResolutionAndCleanupFailuresAreBothPreserved()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(factory.Source.SourceId, coreModel.Id);
		var context = new ItemContext(factory.Source, coreModel, reference);
		var capability = new ThrowingDisposableCapability();
		var capabilityRegistry = new CapabilityBuilder()
			.Add<ThrowingDisposableCapability>(new DelegateCapabilityFactory<ThrowingDisposableCapability>(_ => capability))
			.AddWrapper<ThrowingDisposableCapability>(new DelegateCapabilityWrapper<ThrowingDisposableCapability>((_, _) => throw new InvalidOperationException("resolution failed")))
			.Build();
		using var capabilities = capabilityRegistry.CreateCapabilities(context);

		var error = Assert.Throws<AggregateException>(() => capabilities.Get<ThrowingDisposableCapability>());

		Assert.AreEqual(2, error.InnerExceptions.Count);
		Assert.IsTrue(error.InnerExceptions.Any(static exception => exception.Message == "resolution failed"));
		Assert.IsTrue(error.InnerExceptions.Any(static exception => exception.Message == "cleanup failed"));
		Assert.AreEqual(1, capability.DisposeCount);
	}

	/// <summary>
	/// Test case: multiple options without a combiner fail explicitly.
	/// </summary>
	[TestMethod]
	public void MultipleOptionsWithoutACombinerFailExplicitly()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(factory.Source.SourceId, coreModel.Id);
		var context = new ItemContext(factory.Source, coreModel, reference);
		var capabilityRegistry = new CapabilityBuilder()
			.Add<TestCapability>(new DelegateCapabilityFactory<TestCapability>(_ => new TestCapability("one", [])))
			.Add<TestCapability>(new DelegateCapabilityFactory<TestCapability>(_ => new TestCapability("two", [])))
			.Build();

		using var capabilities = capabilityRegistry.CreateCapabilities(context);
		Assert.Throws<InvalidOperationException>(() => capabilities.Get<TestCapability>());
	}

	/// <summary>
	/// Test case: priority combiner rejects ties at the highest priority.
	/// </summary>
	[TestMethod]
	public void PriorityCombinerRejectsTiesAtTheHighestPriority()
	{
		var factory = new TestModelFactory();
		var coreModel = new TestStorable("item", "Item");
		var reference = new Files.Core.Storage.StorableReference(factory.Source.SourceId, coreModel.Id);
		var context = new ItemContext(factory.Source, coreModel, reference);
		var capabilityRegistry = new CapabilityBuilder()
			.Add<TestCapability>(new DelegateCapabilityFactory<TestCapability>(_ => new TestCapability("one", [])), priority: 10)
			.Add<TestCapability>(new DelegateCapabilityFactory<TestCapability>(_ => new TestCapability("two", [])), priority: 10)
			.SetCombiner<TestCapability>(new PriorityCapabilityCombiner<TestCapability>())
			.Build();

		using var capabilities = capabilityRegistry.CreateCapabilities(context);
		Assert.Throws<InvalidOperationException>(() => capabilities.Get<TestCapability>());
	}

	private sealed class AsyncTestCapability : IAsyncDisposable
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

	private sealed class ThrowingDisposableCapability : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose()
		{
			DisposeCount++;
			throw new InvalidOperationException("cleanup failed");
		}
	}
}
