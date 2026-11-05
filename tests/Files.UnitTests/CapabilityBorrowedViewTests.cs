// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Content;
using Files.Core.Models;
using Files.Core.Storage;

namespace Files.UnitTests;

/// <summary>Tests extensible borrowed views after composition and their ownership contracts.</summary>
[TestClass]
public sealed class CapabilityBorrowedViewTests
{
	/// <summary>A custom view observes the final wrapped implementation while hiding its other roles and retaining owner-controlled cleanup.</summary>
	/// <returns>The asynchronous regression test.</returns>
	[TestMethod]
	public async Task CustomViewRunsAfterCombiningAndWrappingAndIsCached()
	{
		var first = new Checksum("first");
		var second = new Checksum("second");
		var wrapper = new Checksum("wrapped");
		var calls = 0;
		IChecksumSource? composed = null;
		var registry = new CapabilityBuilder()
			.SetBorrowedView<IChecksumSource>(inner =>
			{
				calls++;
				composed = inner;

				return new ChecksumView(inner);
			})
			.Add<IChecksumSource>(new DelegateCapabilityFactory<IChecksumSource>(_ => first))
			.Add<IChecksumSource>(new DelegateCapabilityFactory<IChecksumSource>(_ => second), priority: 10)
			.SetCombiner<IChecksumSource>(new PriorityCapabilityCombiner<IChecksumSource>())
			.AddWrapper<IChecksumSource>(new DelegateCapabilityWrapper<IChecksumSource>((_, inner) =>
			{
				Assert.AreSame(second, inner);

				return wrapper;
			})).Build();
		await using var model = CreateModel(registry);
		Assert.AreEqual(0, calls);
		Assert.IsTrue(model.TryGet<IChecksumSource>(out var borrowed));
		Assert.AreEqual("wrapped", borrowed.Compute());
		Assert.AreSame(wrapper, composed);
		Assert.AreSame(borrowed, model.Get<IChecksumSource>());
		Assert.AreEqual(1, calls);
		Assert.IsFalse(borrowed is IDisposable);
		Assert.IsFalse(borrowed is IAsyncDisposable);
		Assert.IsFalse(borrowed is IWritableContent);
		Assert.IsFalse(borrowed is IStorable);
		Assert.IsNull(model.Get<IWritableContent>());
		await model.DisposeAsync();
		await model.DisposeAsync();
		Assert.AreEqual(1, first.DisposeCount);
		Assert.AreEqual(1, second.DisposeCount);
		Assert.AreEqual(1, wrapper.DisposeCount);
	}

	/// <summary>Replacing a view registration affects only registries built afterward and does not transfer ownership of shared implementations.</summary>
	/// <returns>The asynchronous regression test.</returns>
	[TestMethod]
	public async Task ViewRegistrationsAreSnapshotsAndSharedOwnershipIsPreserved()
	{
		using var shared = new Checksum("shared");
		var builder = new CapabilityBuilder().Add<IChecksumSource>(new DelegateCapabilityFactory<IChecksumSource>(_ => shared), lifetime: CapabilityLifetime.Shared)
			.SetBorrowedView<IChecksumSource>(inner => new ChecksumView(inner, "first:"));
		var first = builder.Build();
		builder.SetBorrowedView<IChecksumSource>(inner => new ChecksumView(inner, "second:"));
		await using var firstModel = CreateModel(first);
		await using var secondModel = CreateModel(builder.Build());
		Assert.AreEqual("first:shared", firstModel.Get<IChecksumSource>()!.Compute());
		Assert.AreEqual("second:shared", secondModel.Get<IChecksumSource>()!.Compute());
		await firstModel.DisposeAsync();
		await secondModel.DisposeAsync();
		Assert.AreEqual(0, shared.DisposeCount);
	}

	/// <summary>Failed view creation cleans up its unpublished owned implementations and permits a later resolution attempt.</summary>
	/// <param name="failure">The invalid view factory behavior.</param>
	/// <returns>The asynchronous regression test.</returns>
	[TestMethod]
	[DataRow("null")]
	[DataRow("throw")]
	[DataRow("disposable")]
	public async Task FailedViewCreationCleansUpAndCanBeRetried(string failure)
	{
		var implementations = new List<Checksum>();
		var invalidView = new Checksum("invalid");
		var fail = true;
		var registry = new CapabilityBuilder().Add<IChecksumSource>(new DelegateCapabilityFactory<IChecksumSource>(_ =>
		{
			var implementation = new Checksum("value");
			implementations.Add(implementation);

			return implementation;
		})).SetBorrowedView<IChecksumSource>(inner =>
		{
			if (fail)
			{
				return failure switch
				{
					"null" => null!,
					"throw" => throw new InvalidOperationException("View creation failed."),
					_ => invalidView,
				};
			}

			return new ChecksumView(inner);
		}).Build();
		await using var model = CreateModel(registry);
		Assert.Throws<InvalidOperationException>(() => model.Get<IChecksumSource>());
		Assert.AreEqual(1, implementations[0].DisposeCount);
		Assert.AreEqual(failure == "disposable" ? 1 : 0, invalidView.DisposeCount);
		fail = false;
		Assert.AreEqual("value", model.Get<IChecksumSource>()!.Compute());
		await model.DisposeAsync();
		Assert.AreEqual(1, implementations[0].DisposeCount);
		Assert.AreEqual(1, implementations[1].DisposeCount);
	}

	/// <summary>A view registration does not create a capability when no factory supplies it.</summary>
	[TestMethod]
	public void ViewRegistrationDoesNotGrantMissingCapabilities()
	{
		var calls = 0;
		var registry = new CapabilityBuilder().SetBorrowedView<IChecksumSource>(inner =>
		{
			calls++;

			return new ChecksumView(inner);
		}).Build();
		using var model = CreateModel(registry);
		Assert.IsNull(model.Get<IChecksumSource>());
		Assert.IsNull(model.Get<IChecksumSource>());
		Assert.AreEqual(0, calls);
	}

	private static IStorableModel CreateModel(CapabilityRegistry registry)
	{
		return new StorableModelFactory(registry).Create(new TestStorageSource(), new TestStorable("item", "Item"));
	}

	private interface IChecksumSource : ICapability
	{
		string Compute();
	}

	private sealed class Checksum(string value) : IChecksumSource, IWritableContent, IStorable, IDisposable
	{
		public string Id => "checksum";
		public string Name => "Checksum";
		public int DisposeCount { get; private set; }

		public string Compute()
		{
			return value;
		}

		public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<Stream>(new MemoryStream());
		}

		public void Dispose()
		{
			DisposeCount++;
		}
	}

	private sealed class ChecksumView(IChecksumSource inner, string prefix = "") : IChecksumSource
	{
		public string Compute()
		{
			return prefix + inner.Compute();
		}
	}
}
