// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Capabilities.Content;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.UnitTests;

/// <summary>Tests archive opening, navigation, catalog fidelity, and scoped ownership.</summary>
[TestClass]
public sealed class ArchiveBrowsingTests
{
	/// <summary>Only explicit unsupported outcomes permit fallback to another reader.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task ReaderFallbackDoesNotHideFailuresOrCredentialChallenges()
	{
		var fallback = new ArchiveTestBackend(_ => new ArchiveOpenResult.Success(new ArchiveTestSession([], backendId: "fallback")), "fallback");
		var primary = new ArchiveTestBackend(_ => ArchiveOpenResult.Unsupported.Instance, "primary", 200);
		using var input = new MemoryStream([1]);
		var reference = new StorableReference(new StorageSourceId("test"), "archive");
		var request = new ArchiveOpenRequest(reference, "archive.zip", input);
		var selector = new ArchiveBackendSelector([fallback, primary]);
		var result = await selector.TryOpenAsync(request);
		Assert.IsInstanceOfType<ArchiveOpenResult.Success>(result);
		await ((ArchiveOpenResult.Success)result).Session.DisposeAsync();
		Assert.AreEqual(1, fallback.OpenCount);

		var error = new IOException("Corrupt input.");
		var failing = new ArchiveTestBackend(_ => new ArchiveOpenResult.Failed(error), "failure", 200);
		var failed = await new ArchiveBackendSelector([failing, fallback]).TryOpenAsync(request);
		Assert.AreSame(error, ((ArchiveOpenResult.Failed)failed).Error);
		Assert.AreEqual(1, fallback.OpenCount);

		var challenge = new ArchiveCredentialChallenge(reference, "archive.zip", 1, false);
		var encrypted = new ArchiveTestBackend(_ => new ArchiveOpenResult.CredentialRequired(challenge), "encrypted", 200);
		Assert.IsInstanceOfType<ArchiveOpenResult.CredentialRequired>(await new ArchiveBackendSelector([encrypted, fallback]).TryOpenAsync(request));
		Assert.AreEqual(1, fallback.OpenCount);
	}

	/// <summary>Physical duplicate records survive projection, along with file/folder and case collisions.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task CatalogPreservesRecordsAndExactReferences()
	{
		ArchiveEntryInfo[] records = [new("0", "same.txt", false, 1), new("1", "same.txt", false, 1), new("2", "Same.txt", false, 1),
			new("3", "node", false, 1), new("4", "node/child.txt", false, 1), new("5", "../unsafe.txt", false, 1)];
		var backend = new ArchiveTestBackend(_ => new ArchiveOpenResult.Success(new ArchiveTestSession(records)));
		await using var source = new ArchiveTestSource();
		var models = backend.CreateModels();
		await using var item = models.Create(source, await source.ResolveAsync(source.Reference));
		await using var archive = await item.Get<IArchiveSource>()!.OpenAsync();
		Assert.AreEqual(6, archive.Entries.Count);
		Assert.IsNull(await archive.Root.GetParentAsync());
		var children = await CollectAsync(archive.Root);
		try
		{
			Assert.AreEqual(5, children.Count);
			Assert.AreEqual(2, children.Count(child => child.Name == "same.txt"));
			Assert.AreEqual(2, children.Count(child => child.Name == "node"));
			Assert.AreEqual(5, children.Select(child => child.Reference).Distinct().Count());
			foreach (var child in children.Where(child => child is IFileModel))
			{
				await using var resolved = await archive.ResolveAsync(child.Reference);
				Assert.IsNull(resolved.Get<IWritableContent>());
				await using var stream = await resolved.Get<IReadableContent>()!.OpenReadAsync();
				using var reader = new StreamReader(stream);
				Assert.AreEqual(((IArchiveEntry)resolved.GetCoreModel()).EntryId, await reader.ReadToEndAsync());
			}

			await using var folder = await archive.OpenFolderAsync("node");
			var nested = await CollectAsync(folder);
			Assert.AreEqual("child.txt", nested.Single().Name);
			await DisposeItemsAsync(nested);
		}
		finally
		{
			await DisposeItemsAsync(children);
		}
	}

	/// <summary>Independent opens have distinct references and do not dispose their shared configured source.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task IndependentViewsAndOwnedStreamsSurviveOtherViewDisposal()
	{
		var sessions = new List<ArchiveTestSession>();
		var backend = new ArchiveTestBackend(_ =>
		{
			var session = new ArchiveTestSession([new("0", "file.txt", false, 1)]);
			sessions.Add(session);

			return new ArchiveOpenResult.Success(session);
		});
		await using var source = new ArchiveTestSource();
		var models = backend.CreateModels();
		await using var item = models.Create(source, await source.ResolveAsync(source.Reference));
		await using var first = await item.Get<IArchiveSource>()!.OpenAsync();
		await using var second = await item.Get<IArchiveSource>()!.OpenAsync();
		Assert.AreNotEqual(first.Root.Reference.SourceId, second.Root.Reference.SourceId);
		var children = await CollectAsync(first.Root);
		var reference = children[0].Reference;
		await using var stream = await children[0].Get<IReadableContent>()!.OpenReadAsync();
		await first.DisposeAsync();
		await first.DisposeAsync();
		Assert.AreEqual(1, sessions[0].DisposeCount);
		Assert.AreEqual(0, sessions[1].DisposeCount);
		Assert.IsFalse(source.IsDisposed);
		Assert.IsTrue(stream.CanRead);
		await Assert.ThrowsAsync<ArgumentException>(async () => await second.ResolveAsync(reference));
		await Assert.ThrowsAsync<ObjectDisposedException>(async () => await children[0].Get<IReadableContent>()!.OpenReadAsync());
		await DisposeItemsAsync(children);
	}

	/// <summary>A nested locator reopens independently and Up returns to the enclosing archive folder.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task NestedArchivesReopenAfterTheOriginalViewCloses()
	{
		var sessions = new List<ArchiveTestSession>();
		var backend = new ArchiveTestBackend(request =>
		{
			var isOuter = request.Input.ReadByte() == 1;
			ArchiveEntryInfo[] entries = isOuter ? [new("7", "folder/inner.7z", false, 1)] : [new("2", "readme.txt", false, 1)];
			var session = new ArchiveTestSession(entries, (_, _) => Task.FromResult<Stream>(new MemoryStream([2])));
			sessions.Add(session);

			return new ArchiveOpenResult.Success(session);
		});
		var source = new ArchiveTestSource();
		var models = backend.CreateModels();
		await using var workspace = new StorageWorkspace([source], models);
		await using var container = await workspace.ResolveAsync(source.Reference);
		var outer = await container.Get<IArchiveSource>()!.OpenAsync();
		await using var folder = await outer.OpenFolderAsync("folder");
		var children = await CollectAsync(folder);
		var innerSource = children[0].Get<IArchiveSource>();
		Assert.IsNotNull(innerSource);
		var locator = innerSource.Locator;
		await using (var inner = await innerSource.OpenAsync())
		{
			await outer.DisposeAsync();
			var innerItems = await CollectAsync(inner.Root);
			Assert.AreEqual("readme.txt", innerItems.Single().Name);
			await DisposeItemsAsync(innerItems);
		}

		await DisposeItemsAsync(children);
		Assert.IsTrue(sessions.All(session => session.DisposeCount == 1));
		var handler = new ArchiveBrowseLocationHandler(workspace, models, new ArchiveBackendSelector([backend]));
		await using var context = await handler.OpenAsync(new ArchiveLocation(locator));
		Assert.AreEqual(new ArchiveLocation(locator.Parent!, "folder"), await ((IBrowseLocationParentResolver)context).GetParentLocationAsync());
		Assert.AreEqual("inner.7z", context.LocationModel!.Name);
	}

	/// <summary>Credential requests are handled before records become visible.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task OpenCredentialsAreScopedToTheArchiveModel()
	{
		var backend = new ArchiveTestBackend(request => request.Credential is null
			? new ArchiveOpenResult.CredentialRequired(new ArchiveCredentialChallenge(request.Archive, request.Name, 1, false))
			: new ArchiveOpenResult.Success(new ArchiveTestSession([])));
		var credentials = new Credentials();
		await using var source = new ArchiveTestSource();
		var models = backend.CreateModels(credentials);
		await using var container = models.Create(source, await source.ResolveAsync(source.Reference));
		await using var archive = await container.Get<IArchiveSource>()!.OpenAsync();
		Assert.AreEqual(1, credentials.Count);
		Assert.AreEqual(2, backend.OpenCount);
	}

	/// <summary>Invalid catalogs release the selected reader even when model creation fails.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task InvalidCatalogConstructionReleasesTheReader()
	{
		var session = new ArchiveTestSession([new("0", "a", false, 1), new("0", "b", false, 1)]);
		var backend = new ArchiveTestBackend(_ => new ArchiveOpenResult.Success(session));
		await using var source = new ArchiveTestSource();
		var models = backend.CreateModels();
		await using var item = models.Create(source, await source.ResolveAsync(source.Reference));
		await Assert.ThrowsAsync<InvalidDataException>(async () => await item.Get<IArchiveSource>()!.OpenAsync());
		Assert.AreEqual(1, session.DisposeCount);
	}

	/// <summary>Archive paths reject traversal and locators have bounded nesting.</summary>
	[TestMethod]
	public void ArchivePathsAndLocatorsHaveExplicitBounds()
	{
		Assert.AreEqual("folder/file.txt", ArchiveEntryPath.Normalize(@"folder\file.txt"));
		Assert.Throws<ArgumentException>(() => ArchiveEntryPath.Normalize("../outside.txt"));
		Assert.Throws<ArgumentException>(() => ArchiveEntryPath.Normalize("/absolute.txt"));
		var locator = new ArchiveLocator(new StorableReference(new StorageSourceId("test"), "archive"));
		for (var index = 1; index < 32; index++)
		{
			locator = locator.Append("0", "inner.zip");
		}

		Assert.Throws<ArgumentException>(() => locator.Append("0", "inner.zip"));
	}

	/// <summary>Credential string representations do not expose secrets.</summary>
	[TestMethod]
	public void ArchiveCredentialDoesNotExposeItsPassword()
	{
		Assert.AreEqual(nameof(ArchiveCredential), new ArchiveCredential("secret").ToString());
	}

	internal static async Task<List<IStorableModel>> CollectAsync(IFolderModel folder)
	{
		var items = new List<IStorableModel>();
		await foreach (var item in folder.GetItemsAsync())
		{
			items.Add(item);
		}

		return items;
	}

	internal static async Task DisposeItemsAsync(IEnumerable<IStorableModel> items)
	{
		foreach (var item in items)
		{
			await item.DisposeAsync();
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
