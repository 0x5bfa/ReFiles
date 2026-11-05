// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Content;
using Files.Core.Models;
using Files.Core.Storage;

namespace Files.UnitTests;

/// <summary>Tests capability contract admission and explicit provider registration.</summary>
[TestClass]
public sealed class CapabilityContractTests
{
	/// <summary>Generic constraints exclude raw storage contracts and arbitrary objects from discovery and registration.</summary>
	/// <param name="type">The non-capability type to reject.</param>
	[TestMethod]
	[DataRow(typeof(object))]
	[DataRow(typeof(IStorable))]
	[DataRow(typeof(IDisposable))]
	[DataRow(typeof(TestStorable))]
	public void NonCapabilityTypesViolateGenericConstraints(Type type)
	{
		foreach (var method in typeof(ICapabilities).GetMethods().Where(static method => method.IsGenericMethodDefinition))
		{
			Assert.Throws<ArgumentException>(() => method.MakeGenericMethod(type));
		}

		foreach (var method in typeof(CapabilityBuilder).GetMethods().Where(static method => method.IsGenericMethodDefinition))
		{
			Assert.Throws<ArgumentException>(() => method.MakeGenericMethod(type));
		}

		Assert.Throws<ArgumentException>(() => typeof(ItemContext).GetMethod(nameof(ItemContext.GetCapability))!.MakeGenericMethod(type));
	}

	/// <summary>Marker and implementation types cannot be discovered or registered even when they satisfy the generic constraint.</summary>
	[TestMethod]
	public void MarkerAndImplementationTypesAreRejected()
	{
		AssertInvalidContract<ICapability>();
		AssertInvalidContract<ReadableStorable>();
	}

	/// <summary>Implementing a capability on a CoreModel does not expose it through an empty registry or an unbound context.</summary>
	[TestMethod]
	public void CoreModelCapabilitiesRequireExplicitRegistration()
	{
		var coreModel = new ReadableStorable();
		var context = CreateContext(coreModel);
		using var capabilities = CapabilityRegistry.Empty.CreateCapabilities(context);

		Assert.IsNull(capabilities.Get<IReadableContent>());
		Assert.IsFalse(capabilities.TryGet<IReadableContent>(out var missing));
		Assert.IsNull(missing);
		Assert.IsNull(context.GetCapability<IReadableContent>());
	}

	/// <summary>Explicit provider registration participates in wrappers without implicitly enabling other CoreModel capabilities.</summary>
	[TestMethod]
	public void ExplicitRegistrationUsesCompositionAndDoesNotAdoptOtherCapabilities()
	{
		var coreModel = new ReadableStorable();
		var wrapper = new ReadableStorable();
		var wrapperCalls = 0;
		var registry = new CapabilityBuilder().Add<IReadableContent>(new DelegateCapabilityFactory<IReadableContent>(context => (IReadableContent)context.CoreModel),
			lifetime: CapabilityLifetime.Shared).AddWrapper<IReadableContent>(new DelegateCapabilityWrapper<IReadableContent>((_, _) =>
			{
				wrapperCalls++;

				return wrapper;
			})).Build();
		using var model = new StorableModelFactory(registry).Create(new TestStorageSource(), coreModel);

		var reader = model.Get<IReadableContent>();
		Assert.IsNotNull(reader);
		Assert.AreNotSame(wrapper, reader);
		Assert.AreSame(reader, model.Get<IReadableContent>());
		Assert.IsFalse(reader is IWritableContent);
		Assert.IsFalse(reader is IStorable);
		Assert.AreEqual(1, wrapperCalls);
		Assert.IsNull(model.Get<IWritableContent>());
	}

	private static ItemContext CreateContext(IStorable coreModel)
	{
		var source = new TestStorageSource();

		return new ItemContext(source, coreModel, new StorableReference(source.SourceId, coreModel.Id));
	}

	private static void AssertInvalidContract<TCapability>() where TCapability : class, ICapability
	{
		var context = CreateContext(new ReadableStorable());
		using var capabilities = CapabilityRegistry.Empty.CreateCapabilities(context);
		Assert.Throws<ArgumentException>(() => capabilities.Get<TCapability>());
		Assert.Throws<ArgumentException>(() => capabilities.TryGet<TCapability>(out _));
		Assert.Throws<ArgumentException>(() => context.GetCapability<TCapability>());
		Assert.Throws<ArgumentException>(() => (context with { Capabilities = capabilities }).GetCapability<TCapability>());

		var builder = new CapabilityBuilder();
		Assert.Throws<ArgumentException>(() => builder.Add<TCapability>(new DelegateCapabilityFactory<TCapability>(_ => null)));
		Assert.Throws<ArgumentException>(() => builder.SetCombiner<TCapability>(new PriorityCapabilityCombiner<TCapability>()));
		Assert.Throws<ArgumentException>(() => builder.AddWrapper<TCapability>(new DelegateCapabilityWrapper<TCapability>((_, capability) => capability)));
	}

	private sealed class ReadableStorable : IStorable, IReadableContent, IWritableContent
	{
		public string Id => "item";
		public string Name => "Item";

		public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<Stream>(new MemoryStream());
		}

		public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<Stream>(new MemoryStream());
		}
	}
}
