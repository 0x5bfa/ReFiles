// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Content;
using Files.Core.Capabilities.Changes;
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

	/// <summary>Owner-only view cleanup runs once before the composed implementation is disposed.</summary>
	[TestMethod]
	public async Task ViewCleanupPrecedesImplementationDisposal()
	{
		var implementation = new Checksum("value");
		var releases = 0;
		var registry = new CapabilityBuilder().Add<IChecksumSource>(new DelegateCapabilityFactory<IChecksumSource>(_ => implementation))
			.SetBorrowedView<IChecksumSource>(inner => new BorrowedCapabilityView<IChecksumSource>(new ChecksumView(inner), () =>
			{
				Assert.AreEqual(0, implementation.DisposeCount);
				releases++;
			})).Build();
		await using var model = CreateModel(registry);
		Assert.AreSame(model.Get<IChecksumSource>(), model.Get<IChecksumSource>());
		await model.DisposeAsync();
		await model.DisposeAsync();
		Assert.AreEqual(1, releases);
		Assert.AreEqual(1, implementation.DisposeCount);
	}

	/// <summary>A failing view cleanup does not prevent underlying implementation cleanup.</summary>
	[TestMethod]
	public async Task ViewCleanupFailureStillDisposesImplementation()
	{
		var implementation = new Checksum("value");
		var registry = new CapabilityBuilder().Add<IChecksumSource>(new DelegateCapabilityFactory<IChecksumSource>(_ => implementation))
			.SetBorrowedView<IChecksumSource>(inner => new BorrowedCapabilityView<IChecksumSource>(new ChecksumView(inner), static () => throw new IOException("detach failed"))).Build();
		var model = CreateModel(registry);
		Assert.IsNotNull(model.Get<IChecksumSource>());
		var error = await Assert.ThrowsAsync<AggregateException>(async () => await model.DisposeAsync());
		Assert.IsTrue(error.Flatten().InnerExceptions.Any(static exception => exception.Message == "detach failed"));
		Assert.AreEqual(1, implementation.DisposeCount);
	}

	/// <summary>Custom contracts cannot publish implementation objects without an explicit borrowed adapter.</summary>
	[TestMethod]
	public void CustomCapabilityRequiresBorrowedView()
	{
		var builder = new CapabilityBuilder().Add<IChecksumSource>(new DelegateCapabilityFactory<IChecksumSource>(_ => new Checksum("value")));
		Assert.Throws<InvalidOperationException>(() => builder.Build());
	}

	/// <summary>Unrelated nondisposable interfaces are rejected and owner-only cleanup runs on failure.</summary>
	[TestMethod]
	public void LeakingViewIsRejectedAndReleased()
	{
		var releases = 0;
		var implementation = new Checksum("value");
		var registry = new CapabilityBuilder().Add<IChecksumSource>(new DelegateCapabilityFactory<IChecksumSource>(_ => implementation))
			.SetBorrowedView<IChecksumSource>(static inner => new LeakingView(inner));
		registry.SetBorrowedView<IChecksumSource>(inner => new BorrowedCapabilityView<IChecksumSource>(new LeakingView(inner), () => releases++));
		using var model = CreateModel(registry.Build());
		Assert.Throws<InvalidOperationException>(() => model.Get<IChecksumSource>());
		Assert.AreEqual(1, releases);
		Assert.AreEqual(1, implementation.DisposeCount);
	}

	/// <summary>Disposing one borrower detaches both handlers while a shared watcher and another borrower remain active.</summary>
	[TestMethod]
	public async Task SharedFolderWatcherDetachesPerBorrower()
	{
		var source = new SharedWatcher();
		var registry = new CapabilityBuilder().Add<IFolderChangeSource>(new DelegateCapabilityFactory<IFolderChangeSource>(_ => source), lifetime: CapabilityLifetime.Shared).Build();
		await using var first = CreateModel(registry);
		await using var second = CreateModel(registry);
		var firstView = first.Get<IFolderChangeSource>()!;
		var secondView = second.Get<IFolderChangeSource>()!;
		var firstCalls = 0;
		var secondCalls = 0;
		firstView.Changed += (_, _) => firstCalls++;
		firstView.Faulted += (_, _) => firstCalls++;
		secondView.Changed += (_, _) => secondCalls++;
		secondView.Faulted += (_, _) => secondCalls++;
		Assert.IsFalse(firstView is IDisposable or IAsyncDisposable);
		Assert.AreEqual(2, source.ChangeSubscribers);
		Assert.AreEqual(2, source.ErrorSubscribers);
		source.Publish();
		Assert.AreEqual(2, firstCalls);
		await first.DisposeAsync();
		await first.DisposeAsync();
		Assert.AreEqual(1, source.ChangeSubscribers);
		Assert.AreEqual(1, source.ErrorSubscribers);
		source.Publish();
		Assert.AreEqual(2, firstCalls);
		Assert.AreEqual(4, secondCalls);
		Assert.Throws<ObjectDisposedException>(() => firstView.StartAsync());
		await second.DisposeAsync();
		Assert.AreEqual(0, source.ChangeSubscribers);
		Assert.AreEqual(0, source.ErrorSubscribers);
		Assert.AreEqual(0, source.DisposeCount);
	}

	private static IStorableModel CreateModel(CapabilityRegistry registry)
	{
		return new StorableModelFactory(registry).Create(new TestStorageSource(), new TestStorable("item", "Item"));
	}

	private interface IChecksumSource : ICapability
	{
		string Compute();
	}

	private sealed class LeakingView(IChecksumSource inner) : IChecksumSource, IStorable
	{
		public string Id => "leaked";
		public string Name => "Leaked";

		public string Compute()
		{
			return inner.Compute();
		}
	}

	private sealed class SharedWatcher : IFolderChangeSource, IDisposable
	{
		public int ChangeSubscribers => Changed?.GetInvocationList().Length ?? 0;
		public int ErrorSubscribers => Faulted?.GetInvocationList().Length ?? 0;
		public int DisposeCount { get; private set; }
		public event EventHandler<FolderChangeEventArgs>? Changed;
		public event EventHandler<FolderChangeErrorEventArgs>? Faulted;

		public ValueTask StartAsync(CancellationToken cancellationToken = default)
		{
			return ValueTask.CompletedTask;
		}

		public void Publish()
		{
			Changed?.Invoke(this, new FolderChangeEventArgs(new FolderChange(FolderChangeKind.Updated, null, null, true)));
			Faulted?.Invoke(this, new FolderChangeErrorEventArgs(new IOException("Watcher failed.")));
		}

		public void Dispose()
		{
			DisposeCount++;
		}
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
