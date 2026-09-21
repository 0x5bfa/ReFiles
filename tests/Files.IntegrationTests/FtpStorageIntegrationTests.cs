// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using Files.Core.Storage;
using Files.Core.Storage.Ftp;

namespace Files.IntegrationTests;

/// <summary>
/// Exercises FTP storage through FluentFTP and a disposable real FTP server.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FtpStorageIntegrationTests
{
	private static FtpTestServer _server = null!;

	/// <summary>
	/// Starts the disposable FTP server used by this test class.
	/// </summary>
	/// <param name="_">The MSTest context.</param>
	/// <returns>A task that represents the asynchronous initialization.</returns>
	[ClassInitialize]
	public static async Task InitializeAsync(TestContext _)
	{
		_server = await FtpTestServer.StartAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Stops the disposable FTP server used by this test class.
	/// </summary>
	/// <returns>A task that represents the asynchronous cleanup.</returns>
	[ClassCleanup]
	public static async Task CleanupAsync()
	{
		if (_server is not null)
		{
			await _server.DisposeAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Verifies create, write, list, read, rename, and permanent delete through the real FTP server.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	[TestCategory("Integration")]
	public async Task RoundTripsUnicodeFileAndRenamesIt()
	{
		await using var source = CreateSource();
		var handler = new FtpStorageOperationHandler(source);
		var content = Encoding.UTF8.GetBytes("ReFiles FTP integration content");
		var folderName = $"roundtrip-{Guid.NewGuid():N}";
		const string fileName = "日本語 & sample.txt";

		var createdFolder = await handler.ExecuteAsync(new CreateItemOperationRequest(source.CreateReference(FtpPath.Root), folderName, StorageItemKind.Folder));
		Assert.IsTrue(createdFolder.Succeeded, createdFolder.Error?.ToString());

		var createdFile = await handler.ExecuteAsync(new CreateItemOperationRequest(createdFolder.ResultItem!, fileName, StorageItemKind.File));
		Assert.IsTrue(createdFile.Succeeded, createdFile.Error?.ToString());

		var file = (FtpFile)await source.ResolveAsync(createdFile.ResultItem!);
		await using (var writeStream = await file.OpenStreamAsync(FileAccess.Write))
		{
			await writeStream.WriteAsync(content);
		}

		var folder = (FtpFolder)await source.ResolveAsync(createdFolder.ResultItem!);
		var items = new List<IStorableChild>();
		await foreach (var item in folder.GetItemsAsync())
		{
			items.Add(item);
		}

		var listedFile = (FtpFile)items.Single(item => item.Name == fileName);
		await using (var readStream = await listedFile.OpenStreamAsync(FileAccess.Read))
		using (var readBuffer = new MemoryStream())
		{
			await readStream.CopyToAsync(readBuffer);
			CollectionAssert.AreEqual(content, readBuffer.ToArray());
		}

		const string renamedFileName = "renamed.txt";
		var renamed = await handler.ExecuteAsync(new RenameOperationRequest(createdFile.ResultItem!, renamedFileName));
		Assert.IsTrue(renamed.Succeeded, renamed.Error?.ToString());
		Assert.AreEqual($"/{folderName}/{renamedFileName}", renamed.ResultItem!.ItemId);
		await Assert.ThrowsAsync<FileNotFoundException>(() => source.ResolveAsync(createdFile.ResultItem!).AsTask());

		var deleted = await handler.ExecuteAsync(new DeleteOperationRequest(renamed.ResultItem!, permanently: true));
		Assert.IsTrue(deleted.Succeeded, deleted.Error?.ToString());
		await Assert.ThrowsAsync<FileNotFoundException>(() => source.ResolveAsync(renamed.ResultItem!).AsTask());
	}

	/// <summary>
	/// Verifies file copy and move operations through the real FTP server.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	[TestCategory("Integration")]
	public async Task CopiesAndMovesFileThroughTheOperationHandler()
	{
		await using var source = CreateSource();
		var handler = new FtpStorageOperationHandler(source);
		var content = Encoding.UTF8.GetBytes("copy and move content");
		var sourceFolderName = $"copy-source-{Guid.NewGuid():N}";
		var destinationFolderName = $"copy-destination-{Guid.NewGuid():N}";

		var sourceFolder = await CreateFolderAsync(handler, source, sourceFolderName);
		var destinationFolder = await CreateFolderAsync(handler, source, destinationFolderName);
		var sourceFile = await handler.ExecuteAsync(new CreateItemOperationRequest(sourceFolder, "source.txt", StorageItemKind.File));
		Assert.IsTrue(sourceFile.Succeeded, sourceFile.Error?.ToString());

		var file = (FtpFile)await source.ResolveAsync(sourceFile.ResultItem!);
		await using (var stream = await file.OpenStreamAsync(FileAccess.Write))
		{
			await stream.WriteAsync(content);
		}

		var copied = await handler.ExecuteAsync(new CopyOperationRequest(sourceFile.ResultItem!, destinationFolder, "copy.txt"));
		Assert.IsTrue(copied.Succeeded, copied.Error?.ToString());

		var moved = await handler.ExecuteAsync(new MoveOperationRequest(copied.ResultItem!, source.CreateReference(FtpPath.Root), "moved.txt"));
		Assert.IsTrue(moved.Succeeded, moved.Error?.ToString());
		await Assert.ThrowsAsync<FileNotFoundException>(() => source.ResolveAsync(copied.ResultItem!).AsTask());

		var movedFile = (FtpFile)await source.ResolveAsync(moved.ResultItem!);
		await using (var stream = await movedFile.OpenStreamAsync(FileAccess.Read))
		using (var buffer = new MemoryStream())
		{
			await stream.CopyToAsync(buffer);
			CollectionAssert.AreEqual(content, buffer.ToArray());
		}

		var deleted = await handler.ExecuteAsync(new DeleteOperationRequest(moved.ResultItem!, permanently: true));
		Assert.IsTrue(deleted.Succeeded, deleted.Error?.ToString());
	}

	/// <summary>
	/// Verifies that the production FluentFTP adapter maps a rejected login to the FTP credential error.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	[TestCategory("Integration")]
	public async Task MapsAuthenticationFailureFromRealServer()
	{
		await using var source = CreateSource("incorrect-password");
		var address = source.CreateAddress(FtpPath.Parse("/authentication-probe.txt"));

		await Assert.ThrowsAsync<FtpAuthenticationRequiredException>(() => source.ResolveAsync(address).AsTask());
	}

	private static FtpStorageSource CreateSource(string? password = null)
	{
		var profile = new FtpConnectionProfile("pyftpdlib", "pyftpdlib", _server.Host, _server.Port, rootPath: "/", userNameHint: _server.UserName);
		var credentials = new StaticFtpCredentialResolver(new FtpCredential(_server.UserName, password ?? _server.Password));

		return new FtpStorageSource(profile, credentials);
	}

	private static async Task<StorableReference> CreateFolderAsync(FtpStorageOperationHandler handler, FtpStorageSource source, string name)
	{
		var result = await handler.ExecuteAsync(new CreateItemOperationRequest(source.CreateReference(FtpPath.Root), name, StorageItemKind.Folder));
		Assert.IsTrue(result.Succeeded, result.Error?.ToString());

		return result.ResultItem!;
	}
}
