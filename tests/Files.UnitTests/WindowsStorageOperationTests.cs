// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using Files.Core.Storage;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for windows storage operation behavior.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsStorageOperationTests
{
	/// <summary>
	/// Test case: create copy move and delete use shell operations.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task CreateCopyMoveAndDeleteUseShellOperations()
	{
		var rootPath = Path.Combine(Path.GetTempPath(), $"Files.Core.OperationTests-{Guid.NewGuid():N}");
		var firstDestinationPath = Path.Combine(rootPath, "first");
		var secondDestinationPath = Path.Combine(rootPath, "second");
		Directory.CreateDirectory(firstDestinationPath);
		Directory.CreateDirectory(secondDestinationPath);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var service = new StorageOperationService([new WindowsStorageOperationHandler(source)]);
			var root = await ResolveReferenceAsync(source, rootPath);
			var firstDestination = await ResolveReferenceAsync(source, firstDestinationPath);
			var secondDestination = await ResolveReferenceAsync(source, secondDestinationPath);

			var created = await service.ExecuteAsync(new CreateItemOperationRequest(root, "created.txt", StorageItemKind.File));
			Assert.IsTrue(created.Succeeded, created.Error?.ToString());
			Assert.IsNotNull(created.ResultItem);
			var createdReference = created.ResultItem!;
			Assert.IsTrue(File.Exists(Path.Combine(rootPath, "created.txt")));

			var copied = await service.ExecuteAsync(new CopyOperationRequest(createdReference, firstDestination, "copied.txt"));
			Assert.IsTrue(copied.Succeeded, copied.Error?.ToString());
			Assert.IsNotNull(copied.ResultItem);
			var copiedReference = copied.ResultItem!;
			Assert.IsTrue(File.Exists(Path.Combine(firstDestinationPath, "copied.txt")));

			var moved = await service.ExecuteAsync(new MoveOperationRequest(copiedReference, secondDestination, "moved.txt"));
			Assert.IsTrue(moved.Succeeded, moved.Error?.ToString());
			Assert.IsNotNull(moved.ResultItem);
			var movedReference = moved.ResultItem!;
			Assert.IsFalse(File.Exists(Path.Combine(firstDestinationPath, "copied.txt")));
			Assert.IsTrue(File.Exists(Path.Combine(secondDestinationPath, "moved.txt")));

			var deleted = await service.ExecuteAsync(new DeleteOperationRequest(movedReference, permanently: true));
			Assert.IsTrue(deleted.Succeeded, deleted.Error?.ToString());
			Assert.IsNull(deleted.ResultItem);
			Assert.IsFalse(File.Exists(Path.Combine(secondDestinationPath, "moved.txt")));

			var createdFolder = await service.ExecuteAsync(new CreateItemOperationRequest(root, "created-folder", StorageItemKind.Folder));
			Assert.IsTrue(createdFolder.Succeeded, createdFolder.Error?.ToString());
			Assert.IsTrue(Directory.Exists(Path.Combine(rootPath, "created-folder")));

			var uniqueCopy = await service.ExecuteAsync(new CopyOperationRequest(createdReference, root, conflictBehavior: StorageConflictBehavior.GenerateUniqueName));
			Assert.IsTrue(uniqueCopy.Succeeded, uniqueCopy.Error?.ToString());
			Assert.IsNotNull(uniqueCopy.ResultItem);
			Assert.AreEqual(Path.Combine(rootPath, "created (2).txt"), uniqueCopy.ResultItem.LastKnownAddress!.Value);
		}
		finally
		{
			Directory.Delete(rootPath, recursive: true);
		}
	}

	/// <summary>
	/// Test case: rename uses shell operation and returns updated reference.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task RenameUsesShellOperationAndReturnsUpdatedReference()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.OperationTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var oldPath = Path.Combine(directoryPath, "old.txt");
		var newPath = Path.Combine(directoryPath, "new.txt");
		File.WriteAllText(oldPath, "content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var original = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, oldPath));
			var request = new RenameOperationRequest(new StorableReference(source.SourceId, original.Id, original.Address), "new.txt");
			var progress = new List<StorageOperationProgress>();
			var service = new StorageOperationService([new WindowsStorageOperationHandler(source)]);

			var result = await service.ExecuteAsync(request, new InlineProgress<StorageOperationProgress>(progress.Add));

			Assert.IsTrue(result.Succeeded, result.Error?.ToString());
			Assert.IsNull(result.Error);
			Assert.IsNotNull(result.ResultItem);
			Assert.IsFalse(File.Exists(oldPath));
			Assert.IsTrue(File.Exists(newPath));
			Assert.AreEqual(original.Id, result.ResultItem!.ItemId);
			Assert.AreEqual(newPath, result.ResultItem.LastKnownAddress!.Value);
			Assert.AreEqual(2, progress.Count);
			Assert.AreEqual(0, progress[0].CompletedItems);
			Assert.AreEqual(1, progress[^1].CompletedItems);
		}
		finally
		{
			if (File.Exists(oldPath))
			{
				File.Delete(oldPath);
			}

			if (File.Exists(newPath))
			{
				File.Delete(newPath);
			}

			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Test case: rename preserves a requested case only name change.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task RenamePreservesARequestedCaseOnlyNameChange()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.OperationTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var originalPath = Path.Combine(directoryPath, "case-name.txt");
		var renamedPath = Path.Combine(directoryPath, "CASE-NAME.TXT");
		File.WriteAllText(originalPath, "content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var original = await ResolveReferenceAsync(source, originalPath);
			var service = new StorageOperationService([new WindowsStorageOperationHandler(source)]);

			var result = await service.ExecuteAsync(new RenameOperationRequest(original, "CASE-NAME.TXT"));

			Assert.IsTrue(result.Succeeded, result.Error?.ToString());
			Assert.IsNotNull(result.ResultItem);
			var actualPath = Directory
				.EnumerateFileSystemEntries(directoryPath)
				.Single();
			Assert.IsTrue(StringComparer.Ordinal.Equals("CASE-NAME.TXT", Path.GetFileName(actualPath)));
			Assert.AreEqual(renamedPath, result.ResultItem.LastKnownAddress!.Value);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Test case: rename hard link returns the renamed directory entry.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task RenameHardLinkReturnsTheRenamedDirectoryEntry()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.HardLinkOperationTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var originalPath = Path.Combine(directoryPath, "original.txt");
		var linkPath = Path.Combine(directoryPath, "link.txt");
		var renamedPath = Path.Combine(directoryPath, "renamed.txt");
		File.WriteAllText(originalPath, "content");
		CreateHardLink(linkPath, originalPath);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var link = await ResolveReferenceAsync(source, linkPath);
			var service = new StorageOperationService([new WindowsStorageOperationHandler(source)]);

			var result = await service.ExecuteAsync(new RenameOperationRequest(link, "renamed.txt"));

			Assert.IsTrue(result.Succeeded, result.Error?.ToString());
			Assert.IsNotNull(result.ResultItem);
			Assert.IsTrue(File.Exists(originalPath));
			Assert.IsFalse(File.Exists(linkPath));
			Assert.IsTrue(File.Exists(renamedPath));
			Assert.AreNotEqual(link.ItemId, result.ResultItem.ItemId);
			Assert.AreEqual(renamedPath, result.ResultItem.LastKnownAddress!.Value);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Test case: rejects path traversal as failed result.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task RejectsPathTraversalAsFailedResult()
	{
		await using var scheduler = new WindowsShellScheduler();
		await using var source = new WindowsStorageSource(scheduler: scheduler);
		var handler = new WindowsStorageOperationHandler(source);
		var request = new RenameOperationRequest(new StorableReference(source.SourceId, "winfs:v1:missing", new StorageAddress(WindowsStorageSource.FileAddressScheme, "C:\\missing.txt")), "..\\escape.txt");

		var result = await handler.ExecuteAsync(request);

		Assert.IsFalse(result.Succeeded);
		Assert.IsInstanceOfType<ArgumentException>(result.Error);
	}

	/// <summary>
	/// Test case: rejects names that windows would normalize or reserve.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task RejectsNamesThatWindowsWouldNormalizeOrReserve()
	{
		await using var scheduler = new WindowsShellScheduler();
		await using var source = new WindowsStorageSource(scheduler: scheduler);
		var handler = new WindowsStorageOperationHandler(source);

		foreach (var newName in new[] { "trailing.", "trailing ", "CON.txt", "LPT9" })
		{
			var request = new RenameOperationRequest(new StorableReference(source.SourceId, "winfs:v1:missing", new StorageAddress(WindowsStorageSource.FileAddressScheme, "C:\\missing.txt")), newName);

			var result = await handler.ExecuteAsync(request);

			Assert.IsFalse(result.Succeeded);
			Assert.IsInstanceOfType<ArgumentException>(result.Error);
		}
	}

	private sealed class InlineProgress<T> : IProgress<T>
	{
		private readonly Action<T> report;

		public InlineProgress(Action<T> report)
		{
			ArgumentNullException.ThrowIfNull(report);

			this.report = report;
		}

		public void Report(T value)
		{
			report(value);
		}
	}

	private static async ValueTask<StorableReference> ResolveReferenceAsync(WindowsStorageSource source, string path)
	{
		var item = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, path));

		return new StorableReference(source.SourceId, item.Id, item.Address);
	}

	private static void CreateHardLink(string linkPath, string existingPath)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = Path.Combine(Environment.SystemDirectory, "fsutil.exe"),
			UseShellExecute = false,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};
		startInfo.ArgumentList.Add("hardlink");
		startInfo.ArgumentList.Add("create");
		startInfo.ArgumentList.Add(linkPath);
		startInfo.ArgumentList.Add(existingPath);

		using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start fsutil.exe.");
		var error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode is not 0)
		{
			throw new IOException($"Could not create the test hard link: {error}");
		}
	}
}
