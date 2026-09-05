// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Changes;
using Files.Core.Storage;
using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for windows folder change behavior.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsFolderChangeTests
{
	/// <summary>
	/// Test case: folder change source reports create rename and delete.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task FolderChangeSourceReportsCreateRenameAndDelete()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.FolderChangeTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var createdPath = Path.Combine(directoryPath, "created.txt");
		var renamedPath = Path.Combine(directoryPath, "renamed.txt");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var folder = (WindowsFolder)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
			var reference = new StorableReference(source.SourceId, folder.Id, folder.Address);
			await using var changeSource = new FolderChangeSourceFactory().Create(new ItemContext(source, folder, reference));

			Assert.IsNotNull(changeSource);
			var createdTask = WaitForChangeAsync(changeSource!, static change => change.Kind is FolderChangeKind.Created);
			await changeSource.StartAsync();
			await Task.Delay(100);
			File.WriteAllText(createdPath, "created");
			var created = await createdTask.WaitAsync(TimeSpan.FromSeconds(10));
			Assert.IsNotNull(created.CurrentItem);

			var renamedTask = WaitForChangeAsync(changeSource, static change => change.Kind is FolderChangeKind.Renamed);
			File.Move(createdPath, renamedPath);
			var renamed = await renamedTask.WaitAsync(TimeSpan.FromSeconds(10));
			Assert.IsTrue(renamed.CurrentItem is not null || renamed.RequiresRefresh);
			Assert.IsTrue(renamed.PreviousItem is not null || renamed.RequiresRefresh);

			var deletedTask = WaitForChangeAsync(changeSource, static change => change.Kind is FolderChangeKind.Deleted);
			File.Delete(renamedPath);
			var deleted = await deletedTask.WaitAsync(TimeSpan.FromSeconds(10));
			Assert.IsTrue(deleted.PreviousItem is not null || deleted.RequiresRefresh);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Test case: shared watcher does not deliver changes to another folder.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SharedWatcherDoesNotDeliverChangesToAnotherFolder()
	{
		var rootPath = Path.Combine(Path.GetTempPath(), $"Files.Core.FolderChangeIsolationTests-{Guid.NewGuid():N}");
		var leftPath = Path.Combine(rootPath, "left");
		var rightPath = Path.Combine(rootPath, "right");
		Directory.CreateDirectory(leftPath);
		Directory.CreateDirectory(rightPath);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var leftFolder = (WindowsFolder)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, leftPath));
			var rightFolder = (WindowsFolder)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, rightPath));
			await using var leftChanges = CreateChangeSource(source, leftFolder);
			await using var rightChanges = CreateChangeSource(source, rightFolder);
			var leftChangeTask = WaitForChangeAsync(leftChanges, static change => change.Kind is FolderChangeKind.Created);
			var rightChangeTask = WaitForChangeAsync(rightChanges, static change => change.Kind is FolderChangeKind.Created);
			await leftChanges.StartAsync();
			await rightChanges.StartAsync();
			await Task.Delay(100);

			var leftFilePath = Path.Combine(leftPath, "left.txt");
			File.WriteAllText(leftFilePath, "left");

			var leftChange = await leftChangeTask.WaitAsync(TimeSpan.FromSeconds(10));
			Assert.IsNotNull(leftChange.CurrentItem);
			Assert.IsFalse(rightChangeTask.IsCompleted);

			var rightFilePath = Path.Combine(rightPath, "right.txt");
			File.WriteAllText(rightFilePath, "right");
			var rightChange = await rightChangeTask.WaitAsync(TimeSpan.FromSeconds(10));
			Assert.IsNotNull(rightChange.CurrentItem);
			StringAssert.Contains(rightChange.CurrentItem!.LastKnownAddress!.Value, rightFilePath);
		}
		finally
		{
			Directory.Delete(rootPath, recursive: true);
		}
	}

	/// <summary>
	/// Test case: shared registration does not duplicate changes.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SharedRegistrationDoesNotDuplicateChanges()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.FolderChangeDuplicateTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var filePath = Path.Combine(directoryPath, "created.txt");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var folder = (WindowsFolder)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
			await using var firstChanges = CreateChangeSource(source, folder);
			await using var secondChanges = CreateChangeSource(source, folder);
			var isCreatedFile = (FolderChange change) =>
				change.Kind is FolderChangeKind.Created
				&& change.CurrentItem?.LastKnownAddress?.Value.Contains(filePath, StringComparison.OrdinalIgnoreCase) is true;
			var firstChangeTask = WaitForChangeAsync(firstChanges, isCreatedFile);
			var secondChangeTask = WaitForChangeAsync(secondChanges, isCreatedFile);
			var firstCount = 0;
			var secondCount = 0;
			firstChanges.Changed += (_, args) =>
			{
				if (isCreatedFile(args.Change))
				{
					Interlocked.Increment(ref firstCount);
				}
			};
			secondChanges.Changed += (_, args) =>
			{
				if (isCreatedFile(args.Change))
				{
					Interlocked.Increment(ref secondCount);
				}
			};
			await firstChanges.StartAsync();
			await secondChanges.StartAsync();
			await Task.Delay(100);

			File.WriteAllText(filePath, "created");
			await firstChangeTask.WaitAsync(TimeSpan.FromSeconds(10));
			await secondChangeTask.WaitAsync(TimeSpan.FromSeconds(10));
			await Task.Delay(1000);
			Assert.AreEqual(1, Volatile.Read(ref firstCount));
			Assert.AreEqual(1, Volatile.Read(ref secondCount));
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	private static IFolderChangeSource CreateChangeSource(WindowsStorageSource source, WindowsFolder folder)
	{
		var reference = new StorableReference(source.SourceId, folder.Id, folder.Address);
		var changeSource = new FolderChangeSourceFactory().Create(new ItemContext(source, folder, reference));

		Assert.IsNotNull(changeSource);

		return changeSource!;
	}

	private static Task<FolderChange> WaitForChangeAsync(IFolderChangeSource changeSource, Func<FolderChange, bool> predicate)
	{
		var completion = new TaskCompletionSource<FolderChange>(TaskCreationOptions.RunContinuationsAsynchronously);
		EventHandler<FolderChangeEventArgs>? handler = null;
		handler = (_, args) =>
		{
			if (!predicate(args.Change))
			{
				return;
			}

			changeSource.Changed -= handler;
			completion.TrySetResult(args.Change);
		};

		changeSource.Changed += handler;

		return completion.Task;
	}
}
