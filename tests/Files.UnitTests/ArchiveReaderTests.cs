// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO.Compression;
using System.Text;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Capabilities.Content;
using Files.Core.Models;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Windows;

namespace Files.UnitTests;

/// <summary>Exercises the shipped archive reader against real containers and independent input lifetimes.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ArchiveReaderTests
{
	/// <summary>Real ZIP records with duplicate names and conflicting paths remain independently readable.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task SharpCompressReaderPreservesDuplicateZipRecords()
	{
		var data = CreateZip(("same.txt", "first"), ("same.txt", "second"), ("Same.txt", "case"), ("node", "file"), ("node/child.txt", "child"));
		await using var source = new ArchiveTestSource(content: data);
		var models = CreateModels();
		await using var container = models.Create(source, await source.ResolveAsync(source.Reference));
		await using var archive = await container.Get<IArchiveSource>()!.OpenAsync();
		Assert.AreEqual(5, archive.Entries.Count);
		var children = await ArchiveBrowsingTests.CollectAsync(archive.Root);
		try
		{
			var contents = new List<string>();
			foreach (var item in children.Where(item => item.Name == "same.txt"))
			{
				await using var stream = await item.Get<IReadableContent>()!.OpenReadAsync();
				using var reader = new StreamReader(stream);
				contents.Add(await reader.ReadToEndAsync());
			}

			CollectionAssert.AreEquivalent(new[] { "first", "second" }, contents);
			Assert.AreEqual(2, children.Count(item => item.Name == "node"));
		}
		finally
		{
			await ArchiveBrowsingTests.DisposeItemsAsync(children);
		}
	}

	/// <summary>CRC failures do not publish an extracted stream.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task SharpCompressReaderRejectsCorruptContentBeforeReturningAStream()
	{
		const string payload = "unique archive payload";
		var data = CreateZip(("file.txt", payload));
		var offset = data.AsSpan().IndexOf(Encoding.UTF8.GetBytes(payload));
		Assert.IsTrue(offset >= 0);
		data[offset] ^= 0x20;
		await using var source = new ArchiveTestSource(content: data);
		var models = CreateModels();
		await using var container = models.Create(source, await source.ResolveAsync(source.Reference));
		await using var archive = await container.Get<IArchiveSource>()!.OpenAsync();
		var children = await ArchiveBrowsingTests.CollectAsync(archive.Root);
		try
		{
			await Assert.ThrowsAsync<ArchiveOpenException>(async () => await children.Single().Get<IReadableContent>()!.OpenReadAsync());
		}
		finally
		{
			await ArchiveBrowsingTests.DisposeItemsAsync(children);
		}
	}

	/// <summary>An encrypted 7z container can recover from a rejected password without exposing reader types.</summary>
	/// <param name="encryptHeaders">Whether record names also require the password.</param>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task SharpCompressReaderOpensEncryptedSevenZipAfterARejectedCredential(bool encryptHeaders)
	{
		var fixture = encryptHeaders ? "Encrypted7zHeaders.7z" : "Encrypted7zData.7z";
		var data = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "ArchiveFixtures", fixture));
		var credentials = new NativeCredentials();
		await using var source = new ArchiveTestSource("encrypted.7z", data);
		var models = CreateModels(credentials);
		await using var container = models.Create(source, await source.ResolveAsync(source.Reference));
		await using var archive = await container.Get<IArchiveSource>()!.OpenAsync();
		var children = await ArchiveBrowsingTests.CollectAsync(archive.Root);
		try
		{
			await using var stream = await children.Single().Get<IReadableContent>()!.OpenReadAsync();
			using var text = new StreamReader(stream);
			Assert.AreEqual("encrypted content", await text.ReadToEndAsync());
			Assert.AreEqual(2, credentials.Count);
		}
		finally
		{
			await ArchiveBrowsingTests.DisposeItemsAsync(children);
		}
	}

	/// <summary>Local archive inputs stay pinned after the original item is disposed and release the file on close.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task LocalInputsArePinnedUntilTheArchiveCloses()
	{
		var path = Path.Combine(Path.GetTempPath(), $"Files.ArchiveNativeTests-{Guid.NewGuid():N}.zip");
		await File.WriteAllBytesAsync(path, CreateZip(("one.txt", "content")));
		try
		{
			await using var source = new WindowsStorageSource();
			var models = CreateModels();
			var item = models.Create(source, await source.ResolveAsync(new StorageAddress("file", path)));
			await using var archive = await item.Get<IArchiveSource>()!.OpenAsync();
			await item.DisposeAsync();
			Assert.Throws<IOException>(() => File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose());
			Assert.AreEqual(1, archive.Entries.Count);
			await archive.DisposeAsync();
			using var writable = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
			Assert.IsTrue(writable.CanWrite);
		}
		finally
		{
			File.Delete(path);
		}
	}

	/// <summary>Read wrappers are respected even when the underlying Windows reader supports pinned input.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task PinnedInputDoesNotBypassReadWrappers()
	{
		var path = Path.Combine(Path.GetTempPath(), $"Files.ArchiveNativeTests-{Guid.NewGuid():N}.zip");
		await File.WriteAllBytesAsync(path, CreateZip(("one.txt", "content")));
		try
		{
			await using var source = new WindowsStorageSource();
			var registry = new CapabilityBuilder().Add<IReadableContent>(new WindowsContentSourceFactory()).Add<IReadableContent>(new ArchiveContentSourceFactory(), lifetime: CapabilityLifetime.Shared)
				.AddWrapper<IReadableContent>(new DelegateCapabilityWrapper<IReadableContent>((_, _) => new DeniedContent())).Add<IArchiveSource>(new ArchiveSourceFactory()).Build();
			var models = new StorableModelFactory(registry);
			await using var item = models.Create(source, await source.ResolveAsync(new StorageAddress("file", path)));
			await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await item.Get<IArchiveSource>()!.OpenAsync());
		}
		finally
		{
			File.Delete(path);
		}
	}

	/// <summary>Only encrypted records may retry an ambiguous integrity failure as a credential challenge.</summary>
	/// <param name="encrypted">Whether the selected record is encrypted.</param>
	/// <param name="expected">Whether another credential can be requested.</param>
	[TestMethod]
	[DataRow(true, true)]
	[DataRow(false, false)]
	public void CredentialRetryDoesNotHideUnencryptedCorruption(bool encrypted, bool expected)
	{
		Assert.AreEqual(expected, Files.Core.Storage.Archives.SharpCompress.SharpCompressArchiveBackend.IsCredentialRetryCandidate(encrypted, new InvalidDataException()));
	}

	private static byte[] CreateZip(params (string Path, string Content)[] entries)
	{
		using var output = new MemoryStream();
		using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
		{
			foreach (var entry in entries)
			{
				using var stream = archive.CreateEntry(entry.Path, System.IO.Compression.CompressionLevel.NoCompression).Open();
				stream.Write(Encoding.UTF8.GetBytes(entry.Content));
			}
		}

		return output.ToArray();
	}

	private static StorableModelFactory CreateModels(IArchiveCredentialResolver? credentials = null)
	{
		var registry = new CapabilityBuilder().Add<IReadableContent>(new WindowsContentSourceFactory())
			.Add<IReadableContent>(new ArchiveContentSourceFactory(), lifetime: CapabilityLifetime.Shared)
			.Add<IReadableContent>(new DelegateCapabilityFactory<IReadableContent>(context => context.Source is ArchiveTestSource ? context.CoreModel as IReadableContent : null),
				lifetime: CapabilityLifetime.Shared).Add<IArchiveSource>(new ArchiveSourceFactory(credentials: credentials)).Build();

		return new StorableModelFactory(registry);
	}

	private sealed class DeniedContent : IReadableContent
	{
		public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
		{
			throw new UnauthorizedAccessException("Denied by the content wrapper.");
		}
	}

	private sealed class NativeCredentials : IArchiveCredentialResolver
	{
		public int Count { get; private set; }

		public ValueTask<ArchiveCredential?> ResolveAsync(ArchiveCredentialChallenge challenge, CancellationToken cancellationToken = default)
		{
			Count++;

			return ValueTask.FromResult<ArchiveCredential?>(new ArchiveCredential(Count == 1 ? "wrong" : "password"));
		}
	}
}
