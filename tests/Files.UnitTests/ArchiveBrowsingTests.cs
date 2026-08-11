// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Files.Core.Browsing;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Archives;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;
using OwlCore.Storage;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for archive browsing behavior.
/// </summary>
[TestClass]
public sealed class ArchiveBrowsingTests
{
	/// <summary>
	/// Test case: selector falls back to the next backend.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SelectorFallsBackToTheNextBackend()
	{
		var source = new TestStorageSource();
		await using var archiveModel = CreateModel(source, new TestStorable("archive", "example.7z"));
		var mount = new TestArchiveMount(archiveModel.Reference, source);
		var primary = new TestArchiveBackend("primary", priority: 200, supportsEncryption: false, _ => ArchiveMountResult.Unsupported.Instance);
		var fallback = new TestArchiveBackend("fallback", priority: 100, supportsEncryption: true, _ => new ArchiveMountResult.Success(mount));
		var selector = new ArchiveBackendSelector([fallback, primary]);

		var result = await selector.TryMountAsync(new ArchiveMountRequest(source, archiveModel));

		Assert.IsInstanceOfType<ArchiveMountResult.Success>(result);
		Assert.AreEqual(1, primary.CallCount);
		Assert.AreEqual(1, fallback.CallCount);
		await mount.DisposeAsync();
	}

	/// <summary>
	/// Test case: encrypted probe skips backends without encryption support.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task EncryptedProbeSkipsBackendsWithoutEncryptionSupport()
	{
		var source = new TestStorageSource();
		await using var archiveModel = CreateModel(source, new TestStorable("archive", "example.7z"));
		var mount = new TestArchiveMount(archiveModel.Reference, source);
		var shell = new TestArchiveBackend("shell", priority: 200, supportsEncryption: false, _ => throw new InvalidOperationException("The Shell backend must be skipped."));
		var sevenZip = new TestArchiveBackend("sevenzip", priority: 100, supportsEncryption: true, _ => new ArchiveMountResult.Success(mount));
		var selector = new ArchiveBackendSelector([shell, sevenZip], new TestArchiveProbe(ArchiveProbeResult.Encrypted));

		var result = await selector.TryMountAsync(new ArchiveMountRequest(source, archiveModel, new ArchiveCredential("password")));

		Assert.IsInstanceOfType<ArchiveMountResult.Success>(result);
		Assert.AreEqual(0, shell.CallCount);
		Assert.AreEqual(1, sevenZip.CallCount);
		await mount.DisposeAsync();
	}

	/// <summary>
	/// Test case: handler obtains credential before publishing items.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task HandlerObtainsCredentialBeforePublishingItems()
	{
		var archive = new TestStorable("archive", "encrypted.7z");
		var source = new ResolvingArchiveSource(archive);
		var modelFactory = new StorableModelFactory();
		await using var workspace = new StorageWorkspace([source], modelFactory);
		var mount = new TestArchiveMount(new StorableReference(source.SourceId, archive.Id), source);
		var backend = new TestArchiveBackend(
			"sevenzip",
			priority: 100,
			supportsEncryption: true,
			request => request.Credential is null
				? new ArchiveMountResult.CredentialRequired(new ArchiveCredentialChallenge(request.Archive, request.ArchiveModel.Name, attempt: 1, previousCredentialRejected: false))
				: new ArchiveMountResult.Success(mount));
		var credentials = new TestArchiveCredentialResolver();
		var handler = new ArchiveBrowseLocationHandler(workspace, modelFactory, new ArchiveBackendSelector([backend]), credentials);
		var location = new ArchiveLocation(new StorableReference(source.SourceId, archive.Id));

		await using var context = await handler.OpenAsync(location);
		var items = new List<IStorableModel>();
		await foreach (var item in context.GetItemsAsync())
		{
			items.Add(item);
		}

		Assert.AreEqual(2, backend.CallCount);
		Assert.AreEqual(1, credentials.CallCount);
		Assert.AreEqual(location, context.Location);
		Assert.IsEmpty(items);

		foreach (var item in items)
		{
			await item.DisposeAsync();
		}
	}

	/// <summary>
	/// Test case: archive context returns logical archive parent.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ArchiveContextReturnsLogicalArchiveParent()
	{
		var archive = new TestStorable("archive", "example.7z");
		var source = new ResolvingArchiveSource(archive);
		var modelFactory = new StorableModelFactory();
		await using var workspace = new StorageWorkspace([source], modelFactory);
		var reference = new StorableReference(source.SourceId, archive.Id);
		var archiveModel = modelFactory.Create(source, archive);
		var mount = new TestArchiveMount(reference, source);
		var folderModel = (IFolderModel)modelFactory.Create(source, mount.Root);
		var location = new ArchiveLocation(reference, "one/two");
		await using var context =
			new ArchiveBrowseLocationContext(location, archiveModel, folderModel, mount, workspace, modelFactory);

		var parent = await context.GetParentLocationAsync();

		Assert.AreEqual(new ArchiveLocation(reference, "one"), parent);
	}

	/// <summary>
	/// Test case: archive source feature includes archive files.
	/// </summary>
	[TestMethod]
	public void ArchiveSourceFeatureIncludesArchiveFiles()
	{
		var source = new TestStorageSource();
		var coreModel = new TestArchiveFile("archive", "example.zip");
		var featureRegistry = new ItemFeatureBuilder()
			.Add<IArchiveSource>(new ArchiveSourceFactory())
			.Build();
		using var model = new StorableModelFactory(featureRegistry)
			.Create(source, coreModel);

		var archiveSource = model.Get<IArchiveSource>();

		Assert.IsNotNull(archiveSource);
		Assert.AreEqual(model.Reference, archiveSource.Archive);
	}

	/// <summary>
	/// Test case: archive source uses windows parsing identity instead of display name.
	/// </summary>
	[TestMethod]
	public void ArchiveSourceUsesWindowsParsingIdentityInsteadOfDisplayName()
	{
		var source = new TestStorageSource();
		var coreModel = new TestWindowsArchiveFile("archive", "example", @"C:\Data\example.zip");
		var featureRegistry = new ItemFeatureBuilder()
			.Add<IArchiveSource>(new ArchiveSourceFactory())
			.Build();
		using var model = new StorableModelFactory(featureRegistry)
			.Create(source, coreModel);

		Assert.IsNotNull(model.Get<IArchiveSource>());
	}

	/// <summary>
	/// Test case: archive paths reject traversal.
	/// </summary>
	[TestMethod]
	public void ArchivePathsRejectTraversal()
	{
		Assert.AreEqual("folder/file.txt", ArchiveEntryPath.Normalize(@"folder\file.txt"));
		Assert.Throws<ArgumentException>(() => ArchiveEntryPath.Normalize("../outside.txt"));
		Assert.Throws<ArgumentException>(() => ArchiveEntryPath.Normalize("/absolute.txt"));
		Assert.Throws<ArgumentException>(() => ArchiveEntryPath.Normalize(@"C:\absolute.txt"));
	}

	/// <summary>
	/// Test case: archive credential does not expose its password.
	/// </summary>
	[TestMethod]
	public void ArchiveCredentialDoesNotExposeItsPassword()
	{
		const string password = "do-not-log-this";
		var credential = new ArchiveCredential(password);

		Assert.AreEqual(nameof(ArchiveCredential), credential.ToString());
		Assert.IsFalse(credential.ToString().Contains(password, StringComparison.Ordinal));
	}

	private static IStorableModel CreateModel(IStorageSource source, IStorable storable)
	{
		return new StorableModelFactory().Create(source, storable);
	}

	private sealed class TestArchiveBackend : IArchiveBackend
	{
		private readonly Func<
			ArchiveMountRequest,
			ArchiveMountResult> mount;

		public string Id { get; }

		public int Priority { get; }

		public bool SupportsEncryptedArchives { get; }

		public int CallCount { get; private set; }

		public TestArchiveBackend(string id, int priority, bool supportsEncryption, Func< ArchiveMountRequest, ArchiveMountResult> mount)
		{
			Id = id;
			Priority = priority;
			SupportsEncryptedArchives = supportsEncryption;
			this.mount = mount;
		}

		public ValueTask<ArchiveMountResult> TryMountAsync(ArchiveMountRequest request, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			CallCount++;

			return ValueTask.FromResult(mount(request));
		}
	}

	private sealed class TestArchiveProbe : IArchiveProbe
	{
		private readonly ArchiveProbeResult result;

		public TestArchiveProbe(ArchiveProbeResult result)
		{
			this.result = result;
		}

		public ValueTask<ArchiveProbeResult> ProbeAsync(ArchiveMountRequest request, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return ValueTask.FromResult(result);
		}
	}

	private sealed class TestArchiveCredentialResolver
		: IArchiveCredentialResolver
	{
		public int CallCount { get; private set; }

		public ValueTask<ArchiveCredential?> ResolveAsync(ArchiveCredentialChallenge challenge, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			CallCount++;

			return ValueTask.FromResult<ArchiveCredential?>(new ArchiveCredential("password"));
		}
	}

	private sealed class TestArchiveMount : IArchiveMount
	{
		private readonly TestArchiveFolder root =
			new("root", "archive");

		public string BackendId => "test";

		public StorableReference Archive { get; }

		public IStorageSource ItemSource { get; }

		public IFolder Root => root;

		public TestArchiveMount(StorableReference archive, IStorageSource source)
		{
			Archive = archive;
			ItemSource = source;
		}

		public ValueTask<IStorable> ResolveAsync(string entryPath, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return string.IsNullOrEmpty(entryPath)
				? ValueTask.FromResult<IStorable>(root)
				: ValueTask.FromException<IStorable>(new FileNotFoundException(entryPath));
		}

		public ValueTask DisposeAsync()
			=> ValueTask.CompletedTask;
	}

	private sealed class TestArchiveFolder : IFolder
	{
		public string Id { get; }

		public string Name { get; }

		public TestArchiveFolder(string id, string name)
		{
			Id = id;
			Name = name;
		}

		public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			await Task.CompletedTask.ConfigureAwait(false);
			yield break;
		}
	}

	private sealed class TestArchiveFile : IFile
	{
		public string Id { get; }

		public string Name { get; }

		public TestArchiveFile(string id, string name)
		{
			Id = id;
			Name = name;
		}

		public Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return Task.FromResult<Stream>(new MemoryStream());
		}
	}

	private sealed class TestWindowsArchiveFile
		: IWindowsStorable, IFile
	{
		public string Id { get; }

		public string Name { get; }

		public string ParsingName { get; }

		public string? FileSystemPath { get; }

		public bool IsFileSystem => true;

		public bool IsStream => true;

		public StorageAddress Address { get; }

		public TestWindowsArchiveFile(string id, string name, string fileSystemPath)
		{
			Id = id;
			Name = name;
			FileSystemPath = fileSystemPath;
			ParsingName = fileSystemPath;
			Address = new StorageAddress(WindowsStorageSource.FileAddressScheme, fileSystemPath);
		}

		public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return Task.FromResult<IFolder?>(null);
		}

		public Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return Task.FromResult<Stream>(new MemoryStream());
		}
	}

	private sealed class ResolvingArchiveSource : IStorageSource
	{
		private readonly IStorable archive;

		public StorageSourceId SourceId { get; } =
			new("archive-test");

		public string SourceType => "archive-test";

		public string DisplayName => "Archive test";

		public ResolvingArchiveSource(IStorable archive)
		{
			this.archive = archive;
		}

		public async IAsyncEnumerable<IFolder> GetRootsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			await Task.CompletedTask.ConfigureAwait(false);
			yield break;
		}

		public bool CanResolve(StorageAddress address)
			=> false;

		public ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default)
			=> ValueTask.FromException<IStorable>(new NotSupportedException());

		public ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return reference.SourceId == SourceId
				&& reference.ItemId == archive.Id
					? ValueTask.FromResult(archive)
					: ValueTask.FromException<IStorable>(new FileNotFoundException(reference.ItemId));
		}

		public ValueTask DisposeAsync()
			=> ValueTask.CompletedTask;
	}
}
