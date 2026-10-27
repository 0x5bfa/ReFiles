// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Capabilities.Content;
using Files.Core.Composition;
using Files.Core.Models;
using Files.Core.Sessions;
using Files.Core.Storage.Archives;
using Files.Core.ViewSettings;

namespace Files.UnitTests;

/// <summary>Protects archive cleanup, reader replacement, and headless navigation.</summary>
[TestClass]
public sealed class ArchiveLifetimeTests
{
	/// <summary>Closing a view waits for its active extraction and releases its reader once.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task DisposalWaitsForActiveExtraction()
	{
		var completion = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
		var session = new ArchiveTestSession([new("0", "one.txt", false, 1)], (_, _) => completion.Task);
		var backend = new ArchiveTestBackend(_ => new ArchiveOpenResult.Success(session));
		await using var source = new ArchiveTestSource();
		var models = backend.CreateModels();
		await using var container = models.Create(source, await source.ResolveAsync(source.Reference));
		await using var archive = await container.Get<IArchiveSource>()!.OpenAsync();
		var children = await ArchiveBrowsingTests.CollectAsync(archive.Root);
		try
		{
			var read = children[0].Get<IReadableContent>()!.OpenReadAsync();
			var disposal = archive.DisposeAsync().AsTask();
			Assert.IsFalse(disposal.IsCompleted);
			Assert.AreEqual(0, session.DisposeCount);
			completion.SetResult(new MemoryStream([1]));
			await using var stream = await read;
			await disposal;
			await archive.DisposeAsync();
			Assert.AreEqual(1, session.DisposeCount);
			Assert.AreEqual(1, stream.ReadByte());
		}
		finally
		{
			if (!completion.Task.IsCompleted)
			{
				completion.SetResult(new MemoryStream());
			}

			await ArchiveBrowsingTests.DisposeItemsAsync(children);
		}
	}

	/// <summary>A reader cannot replace the catalog after published references exist.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task CredentialReopenRejectsAChangedCatalog()
	{
		await using var source = new ArchiveTestSource();
		var first = new ArchiveTestSession([new("0", "one.txt", false, 1)], (_, _) => throw new ArchiveCredentialRequiredException(new(source.Reference, source.Name, 1, true)));
		var replacement = new ArchiveTestSession([new("0", "changed.txt", false, 1)]);
		var backend = new ArchiveTestBackend(request => new ArchiveOpenResult.Success(request.Credential is null ? first : replacement));
		var credentials = new Credentials();
		var models = backend.CreateModels(credentials);
		await using var container = models.Create(source, await source.ResolveAsync(source.Reference));
		await using var archive = await container.Get<IArchiveSource>()!.OpenAsync();
		var children = await ArchiveBrowsingTests.CollectAsync(archive.Root);
		try
		{
			await Assert.ThrowsAsync<ArchiveOpenException>(async () => await children[0].Get<IReadableContent>()!.OpenReadAsync());
			Assert.AreEqual("one.txt", archive.Entries[0].Path);
			Assert.AreEqual(1, credentials.Count);
			Assert.AreEqual(1, first.DisposeCount);
			Assert.AreEqual(1, replacement.DisposeCount);
			await Assert.ThrowsAsync<ArchiveOpenException>(async () => await children[0].Get<IReadableContent>()!.OpenReadAsync());
			Assert.AreEqual(2, backend.OpenCount);
		}
		finally
		{
			await ArchiveBrowsingTests.DisposeItemsAsync(children);
		}
	}

	/// <summary>Late cancellation disposes a reader's unpublished result.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task CancellationDisposesUnpublishedContent()
	{
		using var cancellation = new CancellationTokenSource();
		var stream = new MemoryStream([1]);
		var session = new ArchiveTestSession([new("0", "one.txt", false, 1)], (_, _) =>
		{
			cancellation.Cancel();

			return Task.FromResult<Stream>(stream);
		});
		var backend = new ArchiveTestBackend(_ => new ArchiveOpenResult.Success(session));
		await using var source = new ArchiveTestSource();
		var models = backend.CreateModels();
		await using var container = models.Create(source, await source.ResolveAsync(source.Reference));
		await using var archive = await container.Get<IArchiveSource>()!.OpenAsync();
		var children = await ArchiveBrowsingTests.CollectAsync(archive.Root);
		try
		{
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await children[0].Get<IReadableContent>()!.OpenReadAsync(cancellation.Token));
			Assert.IsFalse(stream.CanRead);
		}
		finally
		{
			await ArchiveBrowsingTests.DisposeItemsAsync(children);
		}
	}

	/// <summary>Archive locations and breadcrumbs retain a nested locator through pane navigation.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task PaneNavigationPreservesNestedArchiveLocators()
	{
		var backend = new ArchiveTestBackend(request => new ArchiveOpenResult.Success(new ArchiveTestSession(request.Input.ReadByte() == 1
			? [new("7", @"folder\inner.7z", false, 1)] : [new("2", "docs/readme.txt", false, 1)], (_, _) => Task.FromResult<Stream>(new MemoryStream([2])))));
		var source = new ArchiveTestSource();
		await using var runtime = new FilesCoreBuilder().AddStorageSource(source).AddArchiveBrowsing([backend]).Build();
		var window = await runtime.ShellSession.CreateWindowAsync(new ArchiveLocation(source.Reference, "folder"));
		var pane = (BrowsePaneSession)window.ActiveTab!.ActivePane!.Content;
		var children = await ArchiveBrowsingTests.CollectAsync((IFolderModel)pane.BrowseSession.Context!.LocationModel!);
		try
		{
			var target = (ArchiveLocation)(await pane.GetItemLocationAsync(children[0].Reference))!;
			Assert.AreEqual("folder/inner.7z", target.Locator.EntryPath);
			await pane.NavigateAsync(new ArchiveLocation(target.Locator, "docs"));
			var breadcrumbs = await pane.Navigation!.GetBreadcrumbsAsync();
			Assert.IsTrue(breadcrumbs.All(entry => entry.Location is ArchiveLocation location && location.Locator == target.Locator));
			Assert.IsTrue(await pane.GoUpAsync());
			Assert.AreEqual(new ArchiveLocation(target.Locator), pane.Location);
			Assert.IsTrue(await pane.GoUpAsync());
			Assert.AreEqual(new ArchiveLocation(target.Locator.Parent!, "folder"), pane.Location);
		}
		finally
		{
			await ArchiveBrowsingTests.DisposeItemsAsync(children);
		}
	}

	/// <summary>Nested archives do not share persisted folder settings with their container or sibling records.</summary>
	[TestMethod]
	public void NestedArchiveSettingsUseTheCompleteContainerChain()
	{
		var root = new ArchiveLocator(new(new("source"), "outer.zip"));
		var first = root.Append("0", "inner.zip");
		var duplicate = root.Append("1", "inner.zip");
		var keys = new[] { root, first, duplicate, first.Append("0", "inner.zip") }.Select(locator => ViewSettingsKey.ForLocation(new ArchiveLocation(locator, "docs"))).ToArray();
		Assert.AreEqual(4, keys.Distinct().Count());
		Assert.AreEqual(keys[1], ViewSettingsKey.ForLocation(new ArchiveLocation(root.Append("0", "inner.zip"), "docs")));
	}

	/// <summary>A failed final transfer response prevents opening a successfully copied remote input.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task InputFinalizationMustSucceedBeforeOpeningTheReader()
	{
		var backend = new ArchiveTestBackend(_ => new ArchiveOpenResult.Success(new ArchiveTestSession([])));
		var readers = new ArchiveBackendSelector([backend]);
		var registry = new CapabilityBuilder().Add<IReadableContent>(new DelegateCapabilityFactory<IReadableContent>(_ => new FinalizationFailureContent()))
			.Add<IArchiveSource>(new ArchiveSourceFactory(readers: readers)).Build();
		await using var source = new ArchiveTestSource(readable: false);
		await using var container = new StorableModelFactory(registry).Create(source, await source.ResolveAsync(source.Reference));
		await Assert.ThrowsAsync<IOException>(async () => await container.Get<IArchiveSource>()!.OpenAsync());
		Assert.AreEqual(0, backend.OpenCount);
	}

	/// <summary>Ambiguous encrypted corruption retains its diagnostic after bounded credential attempts.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task AmbiguousEncryptedCorruptionHasBoundedRetries()
	{
		await using var source = new ArchiveTestSource();
		var corruption = new InvalidDataException("Encrypted CRC validation failed.");
		var backend = new ArchiveTestBackend(_ => new ArchiveOpenResult.Success(new ArchiveTestSession([new("0", "file.txt", false, 1)],
			(_, _) => throw new ArchiveCredentialRequiredException(new(source.Reference, source.Name, 1, false), corruption))));
		var credentials = new Credentials();
		var models = backend.CreateModels(credentials);
		await using var container = models.Create(source, await source.ResolveAsync(source.Reference));
		await using var archive = await container.Get<IArchiveSource>()!.OpenAsync();
		var children = await ArchiveBrowsingTests.CollectAsync(archive.Root);
		try
		{
			var error = await Assert.ThrowsAsync<ArchiveOpenException>(async () => await children[0].Get<IReadableContent>()!.OpenReadAsync());
			Assert.AreSame(corruption, error.InnerException);
			Assert.AreEqual(5, credentials.Count);
		}
		finally
		{
			await ArchiveBrowsingTests.DisposeItemsAsync(children);
		}
	}

	private sealed class FinalizationFailureContent : IReadableContent
	{
		public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<Stream>(new FinalizationFailureStream());
		}
	}

	private sealed class FinalizationFailureStream() : MemoryStream([1])
	{
		public override ValueTask DisposeAsync()
		{
			Dispose();

			return ValueTask.FromException(new IOException("Transfer completion failed."));
		}
	}

	private sealed class Credentials : IArchiveCredentialResolver
	{
		public int Count { get; private set; }

		public ValueTask<ArchiveCredential?> ResolveAsync(ArchiveCredentialChallenge challenge, CancellationToken cancellationToken = default)
		{
			Count++;

			return ValueTask.FromResult<ArchiveCredential?>(new ArchiveCredential("password"));
		}
	}
}
