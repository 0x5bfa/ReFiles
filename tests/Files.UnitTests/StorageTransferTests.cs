// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using System.IO.Compression;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Capabilities.Content;
using Files.Core;
using Files.Core.Storage;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Ftp;
using Files.Core.Storage.Windows;

namespace Files.UnitTests;

/// <summary>Exercises real destination publication across independent storage sources.</summary>
[TestClass]
public sealed class StorageTransferTests
{
	/// <summary>FTP content is streamed into a Windows destination through the composed operation service.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task CopiesFtpContentIntoWindowsAndPreservesConflicts()
	{
		var path = CreateDirectory();
		try
		{
			var sessions = new InMemoryFtpSessionFactory();
			sessions.AddFile("/file.txt", Encoding.UTF8.GetBytes("remote content"));
			await using var runtime = new FilesCoreBuilder().AddWindowsStorage(enablePreviews: false).AddFtpStorage(new("transfer", "FTP", "example.test"), sessionFactory: sessions)
				.AddStorageTransfers().Build();
			var ftp = runtime.Workspace.Sources.OfType<FtpStorageSource>().Single();
			await using var destination = await runtime.Workspace.ResolveAsync(new StorageAddress("file", path));
			var reference = ftp.CreateReference(FtpPath.Parse("/file.txt"));
			var result = await runtime.StorageOperations.ExecuteAsync(new CopyOperationRequest(reference, destination.Reference));
			Assert.IsTrue(result.Succeeded, result.Error?.ToString());
			Assert.AreEqual("remote content", await File.ReadAllTextAsync(Path.Combine(path, "file.txt")));
			Assert.AreEqual(StorageTransferPublication.Committed, result.Transfer!.Publication);
			Assert.AreEqual(result.ResultItem, result.Transfer.Destination);
			var failed = await runtime.StorageOperations.ExecuteAsync(new CopyOperationRequest(reference, destination.Reference));
			Assert.IsFalse(failed.Succeeded);
			Assert.AreEqual(StorageTransferPublication.None, failed.Transfer!.Publication);
			var renamed = await runtime.StorageOperations.ExecuteAsync(new CopyOperationRequest(reference, destination.Reference, conflictBehavior: StorageConflictBehavior.GenerateUniqueName));
			Assert.IsTrue(renamed.Succeeded, renamed.Error?.ToString());
			Assert.AreEqual(2, Directory.GetFiles(path).Length);
			var batch = await runtime.StorageOperations.ExecuteBatchAsync([new CopyOperationRequest(reference, destination.Reference, "batch.txt"),
				new CopyOperationRequest(reference, destination.Reference)]);
			Assert.IsFalse(batch.Succeeded);
			Assert.AreEqual(StorageTransferPublication.Partial, batch.Transfer!.Publication);
			Assert.AreEqual(1, batch.Transfer.CommittedItems);
			Assert.AreEqual("remote content", await File.ReadAllTextAsync(batch.Transfer.Destination!.LastKnownAddress!.Value));
			Assert.IsFalse(runtime.StorageOperations.CanHandle(new MoveOperationRequest(reference, destination.Reference)));
		}
		finally
		{
			Directory.Delete(path, recursive: true);
		}
	}

	/// <summary>Closing the browse view does not lose a captured duplicate archive record.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task CopiesTheCapturedArchiveRecordAfterItsViewCloses()
	{
		var path = CreateDirectory();
		try
		{
			var backend = new ArchiveTestBackend(_ => new ArchiveOpenResult.Success(new ArchiveTestSession([new("0", "same.txt", false, 1), new("1", "same.txt", false, 1)])));
			var source = new ArchiveTestSource();
			await using var runtime = new FilesCoreBuilder().AddWindowsStorage(enablePreviews: false, enableArchives: false).AddStorageSource(source)
				.AddStorageTransfers(new ArchiveBackendSelector([backend])).Build();
			await using var container = await runtime.Workspace.ResolveAsync(source.Reference);
			var archive = await container.Get<IArchiveSource>()!.OpenAsync();
			var children = await ArchiveBrowsingTests.CollectAsync(archive.Root);
			var captured = StorageItemLocator.Capture(children[1]);
			await ArchiveBrowsingTests.DisposeItemsAsync(children);
			await archive.DisposeAsync();
			await using var destination = await runtime.Workspace.ResolveAsync(new StorageAddress("file", path));
			var result = await runtime.StorageOperations.ExecuteAsync(new CopyOperationRequest(captured, destination.Reference));
			Assert.IsTrue(result.Succeeded, result.Error?.ToString());
			Assert.AreEqual("1", await File.ReadAllTextAsync(Path.Combine(path, "same.txt")));
			Assert.AreEqual(2, backend.OpenCount);
			Assert.IsFalse(runtime.StorageOperations.CanHandle(new MoveOperationRequest(captured, destination.Reference)));
		}
		finally
		{
			Directory.Delete(path, recursive: true);
		}
	}

	/// <summary>A failed final source response leaves the existing destination and no staging file.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task FailedSourceCompletionCannotPublishStaging()
	{
		var path = CreateDirectory();
		try
		{
			await File.WriteAllTextAsync(Path.Combine(path, "example.zip"), "original");
			var source = new ArchiveTestSource(readable: false);
			var builder = new FilesCoreBuilder().AddWindowsStorage(enablePreviews: false).AddStorageSource(source).AddStorageTransfers();
			builder.Capabilities.Add<IReadableContent>(new DelegateCapabilityFactory<IReadableContent>(context => context.Source == source ? new FailingContent() : null));
			await using var runtime = builder.Build();
			await using var destination = await runtime.Workspace.ResolveAsync(new StorageAddress("file", path));
			var result = await runtime.StorageOperations.ExecuteAsync(new CopyOperationRequest(source.Reference, destination.Reference, conflictBehavior: StorageConflictBehavior.GenerateUniqueName));
			Assert.IsFalse(result.Succeeded);
			Assert.IsInstanceOfType<IOException>(result.Error);
			Assert.AreEqual(StorageTransferPublication.None, result.Transfer!.Publication);
			Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(path, "example.zip")));
			Assert.AreEqual(1, Directory.GetFiles(path).Length);
		}
		finally
		{
			Directory.Delete(path, recursive: true);
		}
	}

	/// <summary>Cancellation while reading removes staging before any destination publication.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task CancellationCannotPublishPartialContent()
	{
		var path = CreateDirectory();
		try
		{
			using var cancellation = new CancellationTokenSource();
			var source = new ArchiveTestSource("file.txt", readable: false);
			var builder = new FilesCoreBuilder().AddWindowsStorage(enablePreviews: false).AddStorageSource(source).AddStorageTransfers();
			builder.Capabilities.Add<IReadableContent>(new DelegateCapabilityFactory<IReadableContent>(context => context.Source == source ? new CancelingContent(cancellation) : null));
			await using var runtime = builder.Build();
			await using var destination = await runtime.Workspace.ResolveAsync(new StorageAddress("file", path));
			var result = await runtime.StorageOperations.ExecuteAsync(new CopyOperationRequest(source.Reference, destination.Reference), cancellationToken: cancellation.Token);
			Assert.IsFalse(result.Succeeded);
			Assert.IsInstanceOfType<OperationCanceledException>(result.Error);
			Assert.AreEqual(StorageTransferPublication.None, result.Transfer!.Publication);
			Assert.AreEqual(0, Directory.GetFiles(path).Length);
		}
		finally
		{
			Directory.Delete(path, recursive: true);
		}
	}

	/// <summary>An observer failure after commit does not delete or hide the published destination.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task FailureAfterCommitPreservesTheDestinationEffect()
	{
		var path = CreateDirectory();
		try
		{
			var source = new ArchiveTestSource("file.txt", [1, 2, 3]);
			await using var runtime = new FilesCoreBuilder().AddWindowsStorage(enablePreviews: false).AddStorageSource(source).AddStorageTransfers().Build();
			await using var destination = await runtime.Workspace.ResolveAsync(new StorageAddress("file", path));
			var result = await runtime.StorageOperations.ExecuteAsync(new CopyOperationRequest(source.Reference, destination.Reference), new FailingObserver());
			Assert.IsFalse(result.Succeeded);
			Assert.AreEqual(StorageTransferPublication.Committed, result.Transfer!.Publication);
			CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(result.Transfer.Destination!.LastKnownAddress!.Value));
			Assert.AreEqual(1, Directory.GetFiles(path).Length);
		}
		finally
		{
			Directory.Delete(path, recursive: true);
		}
	}

	/// <summary>Native CRC verification must complete before destination publication.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task CorruptArchiveContentLeavesNoPublishedOrStagedFile()
	{
		var path = CreateDirectory();
		try
		{
			const string payload = "unique content for transfer CRC validation";
			using var data = new MemoryStream();
			using (var zip = new ZipArchive(data, ZipArchiveMode.Create, leaveOpen: true))
			using (var output = zip.CreateEntry("file.txt", CompressionLevel.NoCompression).Open())
			{
				output.Write(Encoding.UTF8.GetBytes(payload));
			}

			var bytes = data.ToArray();
			var offset = bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(payload));
			Assert.IsTrue(offset >= 0);
			bytes[offset] ^= 0x20;
			var archivePath = Path.Combine(path, "input.zip");
			await File.WriteAllBytesAsync(archivePath, bytes);
			var outputPath = Directory.CreateDirectory(Path.Combine(path, "output")).FullName;
			await using var runtime = new FilesCoreBuilder().AddWindowsStorage(enablePreviews: false).AddStorageTransfers().Build();
			await using var container = await runtime.Workspace.ResolveAsync(new StorageAddress("file", archivePath));
			await using var destination = await runtime.Workspace.ResolveAsync(new StorageAddress("file", outputPath));
			var locator = new ArchiveRecordLocator(new(container.Reference), "0", "file.txt");
			var result = await runtime.StorageOperations.ExecuteAsync(new CopyOperationRequest(locator, destination.Reference));
			Assert.IsFalse(result.Succeeded);
			Assert.IsInstanceOfType<ArchiveOpenException>(result.Error);
			Assert.AreEqual(StorageTransferPublication.None, result.Transfer!.Publication);
			Assert.AreEqual(0, Directory.GetFiles(outputPath).Length);
		}
		finally
		{
			Directory.Delete(path, recursive: true);
		}
	}

	/// <summary>Malformed archive selectors cannot escape path validation or publish destinations.</summary>
	[TestMethod]
	public void LocatorAndOutcomeContractsRejectAmbiguousValues()
	{
		var reference = new StorableReference(new("source"), "item");
		Assert.Throws<ArgumentException>(() => new ArchiveRecordLocator(new(reference), "0", "../outside.txt"));
		Assert.Throws<ArgumentException>(() => new StorageTransferOutcome(StorageTransferPublication.Committed, StorageTransferPhase.Completed, null, 1));
		Assert.Throws<ArgumentException>(() => new StorageTransferOutcome(StorageTransferPublication.None, StorageTransferPhase.Reading, reference, 1));
	}

	private static string CreateDirectory()
	{
		var path = Path.Combine(Path.GetTempPath(), $"Files.TransferTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);

		return path;
	}

	private sealed class FailingContent : IReadableContent
	{
		public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<Stream>(new FailingCompletionStream());
		}
	}

	private sealed class FailingObserver : IProgress<StorageOperationProgress>
	{
		public void Report(StorageOperationProgress value)
		{
			if (value.CompletedItems == 1)
			{
				throw new IOException("The completion observer failed.");
			}
		}
	}

	private sealed class CancelingContent(CancellationTokenSource cancellation) : IReadableContent
	{
		public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<Stream>(new CancelingStream(cancellation));
		}
	}

	private sealed class CancelingStream(CancellationTokenSource cancellation) : MemoryStream([1, 2, 3])
	{
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			var count = Read(buffer.Span);
			cancellation.Cancel();

			return ValueTask.FromResult(count);
		}
	}

	private sealed class FailingCompletionStream() : MemoryStream([1, 2, 3])
	{
		public override ValueTask DisposeAsync()
		{
			Dispose();

			return ValueTask.FromException(new IOException("The final transfer response failed."));
		}
	}
}
