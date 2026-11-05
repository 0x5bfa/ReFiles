// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Changes;
using Files.Core.Capabilities.Content;
using Files.Core.Models;
using Files.Core.Storage;

namespace Files.UnitTests;

/// <summary>Tests borrowed capability roles, stream access, and owner-controlled disposal.</summary>
[TestClass]
public sealed class CapabilityBoundaryTests
{
	/// <summary>Readers and composed results cannot expose their owners through casts, and model disposal releases each implementation once.</summary>
	/// <returns>The asynchronous regression test.</returns>
	[TestMethod]
	public async Task BorrowedResultsHideOtherRolesAndRetainCompositionOwnership()
	{
		var original = new Content();
		var wrapper = new Content();
		var calls = 0;
		var registry = new CapabilityBuilder().Add<IReadableContent>(new DelegateCapabilityFactory<IReadableContent>(_ => original))
			.AddWrapper<IReadableContent>(new DelegateCapabilityWrapper<IReadableContent>((_, _) =>
			{
				calls++;

				return wrapper;
			})).Build();
		await using var model = new StorableModelFactory(registry).Create(new TestStorageSource(), new TestStorable("item", "Item"));
		var capabilities = model.Capabilities;
		Assert.IsFalse(capabilities is IOwnedCapabilities);
		Assert.IsFalse(capabilities is IDisposable);
		Assert.IsFalse(capabilities is IAsyncDisposable);
		Assert.AreEqual(0, calls);
		Assert.IsTrue(capabilities.TryGet<IReadableContent>(out var reader));
		Assert.AreSame(reader, capabilities.Get<IReadableContent>());
		Assert.AreEqual(1, calls);
		Assert.IsFalse(reader is IWritableContent);
		Assert.IsFalse(reader is IStorable);
		Assert.IsFalse(reader is IDisposable);
		Assert.IsFalse(reader is IAsyncDisposable);
		Assert.IsNull(model.Get<IWritableContent>());
		await using (var stream = await reader.OpenReadAsync())
		{
			Assert.AreEqual(7, stream.ReadByte());
			Assert.IsFalse(stream.CanWrite);
			Assert.Throws<NotSupportedException>(() => stream.WriteByte(1));
			Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
			await Assert.ThrowsAsync<NotSupportedException>(() => stream.WriteAsync(new byte[] { 1 }, 0, 1));
		}

		Assert.AreEqual(0, original.DisposeCount);
		Assert.AreEqual(0, wrapper.DisposeCount);
		await model.DisposeAsync();
		await model.DisposeAsync();
		Assert.AreEqual(1, original.DisposeCount);
		Assert.AreEqual(1, wrapper.DisposeCount);
		Assert.Throws<ObjectDisposedException>(() => capabilities.Get<IReadableContent>());
	}

	/// <summary>Change events use the borrowed facade as sender and keep subscription disposal with the model.</summary>
	/// <returns>The asynchronous regression test.</returns>
	[TestMethod]
	public async Task ChangeNotificationsDoNotExposeTheOwnedSubscription()
	{
		var changes = new TestFolderChangeSource();
		var registry = new CapabilityBuilder().Add<IFolderChangeSource>(new DelegateCapabilityFactory<IFolderChangeSource>(_ => changes)).Build();
		await using var model = new StorableModelFactory(registry).Create(new TestStorageSource(), new TestStorable("folder", "Folder"));
		var borrowed = model.Get<IFolderChangeSource>()!;
		Assert.IsFalse(borrowed is IDisposable);
		Assert.IsFalse(borrowed is IAsyncDisposable);
		object? changedSender = null;
		object? faultSender = null;
		EventHandler<FolderChangeEventArgs> handler = (sender, _) => changedSender = sender;
		borrowed.Changed += handler;
		borrowed.Faulted += (sender, _) => faultSender = sender;
		await borrowed.StartAsync();
		Assert.IsTrue(changes.IsStarted);
		changes.RaiseChange();
		changes.RaiseFault(new IOException("test"));
		Assert.AreSame(borrowed, changedSender);
		Assert.AreSame(borrowed, faultSender);
		borrowed.Changed -= handler;
		changedSender = null;
		changes.RaiseChange();
		Assert.IsNull(changedSender);
		Assert.IsFalse(changes.IsDisposed);
		await model.DisposeAsync();
		Assert.IsTrue(changes.IsDisposed);
	}

	private sealed class Content : IReadableContent, IWritableContent, IStorable, IDisposable
	{
		public string Id => "content";
		public string Name => "Content";
		public int DisposeCount { get; private set; }

		public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<Stream>(new MemoryStream([7, 8]));
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
}
