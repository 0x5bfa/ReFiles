// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using Files.Core.Storage;
using Files.Core.Storage.Ftp;

namespace Files.UnitTests;

[TestClass]
public sealed class FtpStorageOperationTests
{
	[TestMethod]
	public async Task ExecutesCreateCopyMoveAndPermanentDelete()
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFile("/source.txt", Encoding.UTF8.GetBytes("content"));
		sessions.AddFolder("/first");
		sessions.AddFolder("/second");
		var profile = new FtpConnectionProfile("operations", "Operations FTP", "example.test");
		await using var source = new FtpStorageSource(profile, sessionFactory: sessions);
		var handler = new FtpStorageOperationHandler(source);
		var root = source.CreateReference(FtpPath.Root);
		var first = source.CreateReference(FtpPath.Parse("/first"));
		var second = source.CreateReference(FtpPath.Parse("/second"));

		var created = await handler.ExecuteAsync(new CreateItemOperationRequest(root, "created", StorageItemKind.Folder));
		Assert.IsTrue(created.Succeeded, created.Error?.ToString());
		Assert.IsTrue(sessions.Contains("/created"));

		var copied = await handler.ExecuteAsync(new CopyOperationRequest(source.CreateReference(FtpPath.Parse("/source.txt")), first, "copy.txt"));
		Assert.IsTrue(copied.Succeeded, copied.Error?.ToString());
		CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("content"), sessions.ReadContent("/first/copy.txt"));
		Assert.IsFalse(sessions.Paths.Any(path => path.Contains(".files-copy-", StringComparison.Ordinal)));

		var moved = await handler.ExecuteAsync(new MoveOperationRequest(copied.ResultItem!, second, "moved.txt"));
		Assert.IsTrue(moved.Succeeded, moved.Error?.ToString());
		Assert.IsFalse(sessions.Contains("/first/copy.txt"));
		Assert.IsTrue(sessions.Contains("/second/moved.txt"));

		var recycled = await handler.ExecuteAsync(new DeleteOperationRequest(moved.ResultItem!));
		Assert.IsFalse(recycled.Succeeded);
		Assert.IsInstanceOfType<NotSupportedException>(recycled.Error);
		Assert.IsTrue(sessions.Contains("/second/moved.txt"));

		var deleted = await handler.ExecuteAsync(new DeleteOperationRequest(moved.ResultItem!, permanently: true));
		Assert.IsTrue(deleted.Succeeded, deleted.Error?.ToString());
		Assert.IsFalse(sessions.Contains("/second/moved.txt"));
	}

	[TestMethod]
	public async Task GeneratesAUniqueRemoteName()
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFile("/report.txt", [1]);
		var profile = new FtpConnectionProfile("collisions", "Collisions FTP", "example.test");
		await using var source = new FtpStorageSource(profile, sessionFactory: sessions);
		var handler = new FtpStorageOperationHandler(source);

		var result = await handler.ExecuteAsync(
			new CopyOperationRequest(
				source.CreateReference(FtpPath.Parse("/report.txt")),
				source.CreateReference(FtpPath.Root),
				conflictBehavior:
					StorageConflictBehavior.GenerateUniqueName));

		Assert.IsTrue(result.Succeeded, result.Error?.ToString());
		Assert.AreEqual("/report (2).txt", result.ResultItem!.ItemId);
		Assert.IsTrue(sessions.Contains("/report (2).txt"));
	}

	[TestMethod]
	public async Task RecursivelyCopiesAFolderWithoutPublishingTemporaryItems()
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFolder("/source");
		sessions.AddFolder("/source/nested");
		sessions.AddFile("/source/nested/item.bin", [1, 2, 3]);
		var profile = new FtpConnectionProfile("folder-copy", "Folder copy FTP", "example.test");
		await using var source = new FtpStorageSource(profile, sessionFactory: sessions);
		var handler = new FtpStorageOperationHandler(source);

		var result = await handler.ExecuteAsync(
			new CopyOperationRequest(source.CreateReference(FtpPath.Parse("/source")), source.CreateReference(FtpPath.Root), "destination"));

		Assert.IsTrue(result.Succeeded, result.Error?.ToString());
		Assert.IsTrue(sessions.Contains("/destination"));
		CollectionAssert.AreEqual(new byte[] {1, 2, 3}, sessions.ReadContent("/destination/nested/item.bin"));
		Assert.IsFalse(sessions.Paths.Any(path => path.Contains(".files-copy-", StringComparison.Ordinal)));
	}
}
