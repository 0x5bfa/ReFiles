// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO.Compression;
using System.Text;
using Files.BackgroundServer;
using Files.Core.Storage;
using Files.Core.Storage.Ftp;
using Files.Core.Storage.Windows;

namespace Files.IntegrationTests;

/// <summary>Exercises the production server job host, value protocol, native reader, and real FTP connections.</summary>
[TestClass]
[DoNotParallelize]
public sealed class StorageTransferJobTests
{
	/// <summary>A server-owned FTP source survives disposal of the client source and connection lease.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task FtpCopyCompletesAfterTheClientDisconnects()
	{
		await using var ftpServer = await FtpTestServer.StartAsync();
		var profile = new FtpConnectionProfile($"transfer-{Guid.NewGuid():N}", "Transfer", ftpServer.Host, ftpServer.Port);
		var credential = new FtpCredential(ftpServer.UserName, ftpServer.Password);
		var client = new FtpStorageSource(profile, new StaticFtpCredentialResolver(credential));
		var reference = client.CreateReference(FtpPath.Parse("/remote.txt"));
		var created = await new FtpStorageOperationHandler(client).ExecuteAsync(new CreateItemOperationRequest(client.CreateReference(FtpPath.Root), "remote.txt", StorageItemKind.File));
		Assert.IsTrue(created.Succeeded, created.Error?.ToString());
		var file = (FtpFile)await client.ResolveAsync(reference);
		await using (var output = await file.OpenWriteAsync())
		{
			await output.WriteAsync(Encoding.UTF8.GetBytes("server-owned transfer"));
		}

		var address = client.CreateAddress(FtpPath.Parse("/remote.txt"));
		await client.DisposeAsync();
		var configuration = new FtpConnectionData
		{
			ConnectionId = profile.ConnectionId, DisplayName = profile.DisplayName, Host = profile.Host, Port = profile.Port, SecurityMode = (int)profile.SecurityMode,
			RootPath = profile.RootPath.Value, UserNameHint = string.Empty, PathComparison = (int)profile.PathComparison,
		};
		Program.Storage.Value.RegisterFtpConnection(configuration, credential.UserName, "outdated-password");
		var registered = Program.Storage.Value.RegisterFtpConnection(configuration, credential.UserName, credential.Password);
		Assert.Throws<InvalidOperationException>(() => Program.Storage.Value.RegisterFtpConnection(configuration, "different-account", credential.Password));
		Assert.AreEqual(reference.SourceId.Value, registered);
		var directory = CreateDirectory();
		try
		{
			await using var windows = new WindowsStorageSource();
			var destination = await ToDataAsync(windows, directory);
			var item = new StorageTransferData { Kind = StorageOperationKind.Copy, SourceKind = StorageTransferSourceKind.ConfiguredItem, Destination = destination,
				Name = string.Empty, DisplayName = "remote.txt",
				Root = new StorageReferenceData { SourceId = registered, ItemId = reference.ItemId, HasAddress = true, AddressScheme = address.Scheme, AddressValue = address.Value } };
			using var lifetime = new ServerLifetime(TimeSpan.FromSeconds(30));
			var clientLease = lifetime.Acquire(isClient: true);
			var host = new OperationHost(lifetime);
			var id = host.Submit(new StorageTransferBatch(Guid.NewGuid(), [item], []));
			clientLease.Dispose();
			var result = await WaitAsync(host, id);
			Assert.AreEqual(OperationState.Succeeded, result.State, result.ErrorMessage);
			Assert.AreEqual("server-owned transfer", await File.ReadAllTextAsync(Path.Combine(directory, "remote.txt")));
			Assert.AreEqual((int)StorageTransferPublication.Committed, result.TransferPublication);
			Assert.AreEqual(1, result.TransferCommittedItems);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	/// <summary>The exact duplicate record is copied and remains visible when the next source fails.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task ArchiveBatchRetainsCommittedEffectsBeforeAFailure()
	{
		var directory = CreateDirectory();
		var input = Path.Combine(directory, "input.zip");
		var output = Path.Combine(directory, "output");
		Directory.CreateDirectory(output);
		try
		{
			using (var stream = File.Create(input))
			using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				foreach (var text in new[] { "first", "second" })
				{
					using var writer = new StreamWriter(archive.CreateEntry("same.txt").Open());
					writer.Write(text);
				}
			}

			await using var windows = new WindowsStorageSource();
			var root = await ToDataAsync(windows, input);
			var destination = await ToDataAsync(windows, output);
			var selected = new StorageTransferData { Kind = StorageOperationKind.Copy, SourceKind = StorageTransferSourceKind.ArchiveRecord, Root = root, RecordId = "1", EntryPath = "same.txt",
				Destination = destination,
				Name = string.Empty, DisplayName = "same.txt" };
			var missing = selected;
			missing.RecordId = "99";
			missing.Name = "missing.txt";
			using var lifetime = new ServerLifetime(TimeSpan.FromSeconds(30));
			var host = new OperationHost(lifetime);
			var id = host.Submit(new StorageTransferBatch(Guid.NewGuid(), [selected, missing], []));
			var result = await WaitAsync(host, id);
			Assert.AreEqual(OperationState.Failed, result.State);
			Assert.AreEqual((int)StorageTransferPublication.Partial, result.TransferPublication);
			Assert.AreEqual(1, result.TransferCommittedItems);
			Assert.AreEqual("second", await File.ReadAllTextAsync(Path.Combine(output, "same.txt")));
			Assert.AreEqual(1, Directory.GetFiles(output).Length);
			Assert.IsFalse(string.IsNullOrEmpty(result.TransferDestination.ItemId));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	/// <summary>Malformed chains and archive Move cannot be admitted as ordinary container operations.</summary>
	[TestMethod]
	public void RejectsMalformedTransferValuesBeforeExecution()
	{
		var reference = new StorageReferenceData { SourceId = "windows", ItemId = "item" };
		var data = new StorageTransferData { Kind = StorageOperationKind.Copy, SourceKind = StorageTransferSourceKind.ArchiveRecord, Root = reference, Destination = reference,
			RecordId = "0", EntryPath = "../escape.txt" };
		Assert.Throws<ArgumentException>(() => StorageTransferBatch.CreateRequest(data, []));
		data.EntryPath = "file.txt";
		data.ArchiveStepOffset = int.MaxValue;
		Assert.Throws<ArgumentException>(() => StorageTransferBatch.CreateRequest(data, []));
		data.ArchiveStepOffset = 0;
		data.Kind = StorageOperationKind.Move;
		Assert.Throws<NotSupportedException>(() => StorageTransferBatch.CreateRequest(data, []));
	}

	private static async Task<OperationSummary> WaitAsync(OperationHost host, Guid id)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		while (true)
		{
			var list = host.GetOperations();
			var summary = list.GetItems().Single(item => item.Id == id);
			if (summary.State is OperationState.Succeeded or OperationState.Failed or OperationState.Canceled)
			{
				return summary;
			}

			await host.WaitForChangesAsync(list.ServerInstanceId, list.Revision, timeout.Token);
		}
	}

	private static async Task<StorageReferenceData> ToDataAsync(WindowsStorageSource source, string path)
	{
		var item = await source.ResolveAsync(new StorageAddress("file", path));

		return StorageOperationExecutor.ToData(new StorableReference(source.SourceId, item.Id, new StorageAddress("file", path)));
	}

	private static string CreateDirectory()
	{
		var path = Path.Combine(Path.GetTempPath(), $"Files.TransferJobTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);

		return path;
	}
}
