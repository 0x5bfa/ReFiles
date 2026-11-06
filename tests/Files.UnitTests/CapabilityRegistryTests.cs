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
			.SetBorrowedView<ITestCapability>(static inner => new TestView())
			.SetBorrowedView<IAsyncTestCapability>(static _ => new AsyncView())
			.SetBorrowedView<IThrowingDisposableCapability>(static _ => new ThrowingView())
			.Add<ITestCapability>(new DelegateCapabilityFactory<ITestCapability>(_ =>
			{
				createCount++;

				return capability;
			}))
			.Build();

		using var capabilities = capabilityRegistry.CreateCapabilities(context);
		Assert.AreEqual(0, createCount);

		var borrowed = capabilities.Get<ITestCapability>();
		Assert.IsInstanceOfType<TestView>(borrowed);
		Assert.AreSame(borrowed, capabilities.Get<ITestCapability>());
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
			.SetBorrowedView<ITestCapability>(static inner => new TestView())
			.SetBorrowedView<IAsyncTestCapability>(static _ => new AsyncView())
			.SetBorrowedView<IThrowingDisposableCapability>(static _ => new ThrowingView())
			.Add<ITestCapability>(new DelegateCapabilityFactory<ITestCapability>(_ => innerCapability))
			.AddWrapper<ITestCapability>(new DelegateCapabilityWrapper<ITestCapability>((_, _) => wrapper))
			.Build();

		using (var capabilities = capabilityRegistry.CreateCapabilities(context))
		{
			Assert.IsInstanceOfType<TestView>(capabilities.Get<ITestCapability>());
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
			.SetBorrowedView<ITestCapability>(static inner => new TestView())
			.SetBorrowedView<IAsyncTestCapability>(static _ => new AsyncView())
			.SetBorrowedView<IThrowingDisposableCapability>(static _ => new ThrowingView())
			.Add<ITestCapability>(new DelegateCapabilityFactory<ITestCapability>(_ => capability), lifetime: CapabilityLifetime.Shared)
			.Build();

		using (var capabilities = capabilityRegistry.CreateCapabilities(context))
		{
			Assert.IsInstanceOfType<TestView>(capabilities.Get<ITestCapability>());
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
			.SetBorrowedView<ITestCapability>(static inner => new TestView())
			.SetBorrowedView<IAsyncTestCapability>(static _ => new AsyncView())
			.SetBorrowedView<IThrowingDisposableCapability>(static _ => new ThrowingView())
			.Add<IAsyncTestCapability>(new DelegateCapabilityFactory<IAsyncTestCapability>(_ => capability))
			.Build();
		var capabilities = capabilityRegistry.CreateCapabilities(context);

		Assert.IsInstanceOfType<AsyncView>(capabilities.Get<IAsyncTestCapability>());
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
			.SetBorrowedView<ITestCapability>(static inner => new TestView())
			.SetBorrowedView<IAsyncTestCapability>(static _ => new AsyncView())
			.SetBorrowedView<IThrowingDisposableCapability>(static _ => new ThrowingView())
			.Add<IThrowingDisposableCapability>(new DelegateCapabilityFactory<IThrowingDisposableCapability>(_ => capability))
			.AddWrapper<IThrowingDisposableCapability>(new DelegateCapabilityWrapper<IThrowingDisposableCapability>((_, _) => throw new InvalidOperationException("resolution failed")))
			.Build();
		using var capabilities = capabilityRegistry.CreateCapabilities(context);

		var error = Assert.Throws<AggregateException>(() => capabilities.Get<IThrowingDisposableCapability>());

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
			.SetBorrowedView<ITestCapability>(static inner => new TestView())
			.SetBorrowedView<IAsyncTestCapability>(static _ => new AsyncView())
			.SetBorrowedView<IThrowingDisposableCapability>(static _ => new ThrowingView())
			.Add<ITestCapability>(new DelegateCapabilityFactory<ITestCapability>(_ => new TestCapability("one", [])))
			.Add<ITestCapability>(new DelegateCapabilityFactory<ITestCapability>(_ => new TestCapability("two", [])))
			.Build();

		using var capabilities = capabilityRegistry.CreateCapabilities(context);
		Assert.Throws<InvalidOperationException>(() => capabilities.Get<ITestCapability>());
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
			.SetBorrowedView<ITestCapability>(static inner => new TestView())
			.SetBorrowedView<IAsyncTestCapability>(static _ => new AsyncView())
			.SetBorrowedView<IThrowingDisposableCapability>(static _ => new ThrowingView())
			.Add<ITestCapability>(new DelegateCapabilityFactory<ITestCapability>(_ => new TestCapability("one", [])), priority: 10)
			.Add<ITestCapability>(new DelegateCapabilityFactory<ITestCapability>(_ => new TestCapability("two", [])), priority: 10)
			.SetCombiner<ITestCapability>(new PriorityCapabilityCombiner<ITestCapability>())
			.Build();

		using var capabilities = capabilityRegistry.CreateCapabilities(context);
		Assert.Throws<InvalidOperationException>(() => capabilities.Get<ITestCapability>());
	}

	private interface IAsyncTestCapability : ICapability
	{
	}

	private interface IThrowingDisposableCapability : ICapability
	{
	}

	private sealed class TestView : ITestCapability { }

	private sealed class AsyncView : IAsyncTestCapability { }

	private sealed class ThrowingView : IThrowingDisposableCapability { }

	private sealed class AsyncTestCapability : IAsyncTestCapability, IAsyncDisposable
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

	private sealed class ThrowingDisposableCapability : IThrowingDisposableCapability, IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose()
		{
			DisposeCount++;
			throw new InvalidOperationException("cleanup failed");
		}
	}
}
