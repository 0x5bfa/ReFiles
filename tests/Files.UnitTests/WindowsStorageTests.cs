// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using Files.Core.Storage;

namespace Files.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class WindowsStorageTests
{
	[TestMethod]
	public async Task FileSystemFolderEnumerationBatchesAndFiltersItems()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.UnitTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);

		try
		{
			for (var index = 0; index < 40; index++)
			{
				File.WriteAllText(Path.Combine(directoryPath, $"file-{index:D2}.txt"), index.ToString());
			}

			Directory.CreateDirectory(Path.Combine(directoryPath, "folder"));
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var folder = await source.ResolveAsync(new Files.Core.Storage.StorageAddress("file", directoryPath));
			var coreFolder = (IFolder)folder;

			var all = new List<IStorableChild>();
			await foreach (var item in coreFolder.GetItemsAsync())
			{
				all.Add(item);
			}

			var files = new List<IStorableChild>();
			await foreach (var item in coreFolder.GetItemsAsync(StorableType.File))
			{
				files.Add(item);
			}

			Assert.AreEqual(41, all.Count);
			Assert.AreEqual(40, files.Count);
		Assert.AreEqual(40, all.Count(item => item is IFile));
		Assert.AreEqual(1, all.Count(item => item is IFolder));
			Assert.AreEqual(41, all.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	[TestMethod]
	public async Task HardLinkedFileEnumerationUsesDistinctDirectoryEntryIdentities()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.HardLinkTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var originalPath = Path.Combine(directoryPath, "original.txt");
		var linkPath = Path.Combine(directoryPath, "link.txt");

		try
		{
			File.WriteAllText(originalPath, "content");
			CreateHardLink(linkPath, originalPath);

			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var folder = (IFolder)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));

			var items = new List<IStorableChild>();
			await foreach (var item in folder.GetItemsAsync(StorableType.File))
			{
				items.Add(item);
			}

			Assert.AreEqual(2, items.Count);
			Assert.AreEqual(2, items.Select(static item => item.Id) .Distinct(StringComparer.Ordinal) .Count());
			Assert.IsTrue(items.All(static item => item.Id.StartsWith("winshell-address:v1:", StringComparison.Ordinal)));
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	[TestMethod]
	public async Task FileSystemStreamReadsAndSeeks()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.UnitTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var filePath = Path.Combine(directoryPath, "content.bin");
		var expected = Enumerable.Range(0, 256).Select(static value => (byte)value).ToArray();
		File.WriteAllBytes(filePath, expected);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var storable = await source.ResolveAsync(new Files.Core.Storage.StorageAddress("file", filePath));
			var file = (IFile)storable;
			using var stream = await file.OpenStreamAsync(FileAccess.Read);
			Assert.AreEqual(expected.Length, stream.Length);
			stream.Seek(32, SeekOrigin.Begin);
			var buffer = new byte[16];
			Assert.AreEqual(16, stream.Read(buffer, 0, buffer.Length));
			CollectionAssert.AreEqual(expected.Skip(32).Take(16).ToArray(), buffer);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	[TestMethod]
	public async Task InjectedSchedulerIsBorrowedByTheSource()
	{
		await using var scheduler = new WindowsShellScheduler();
		var source = new WindowsStorageSource(scheduler: scheduler);
		await source.DisposeAsync();

		var result = await scheduler.InvokeAsync(static () => true);
		Assert.IsTrue(result);
	}

	[TestMethod]
	public async Task DirectResolutionAndEnumerationShareTheSameIdentity()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.IdentityTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var filePath = Path.Combine(directoryPath, "item.txt");
		File.WriteAllText(filePath, "content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var folder = (IFolder)await source.ResolveAsync(new Files.Core.Storage.StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
			var enumerated = await FindItemAsync(folder, "item.txt");
			var direct = (IWindowsStorable)await source.ResolveAsync(new Files.Core.Storage.StorageAddress(WindowsStorageSource.FileAddressScheme, filePath));

			Assert.IsNotNull(enumerated);
			Assert.AreEqual(direct.Id, enumerated!.Id);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	[TestMethod]
	public async Task VirtualShellItemUsesVersionedAddressIdentity()
	{
		await using var scheduler = new WindowsShellScheduler();
		await using var source = new WindowsStorageSource(scheduler: scheduler);
		IWindowsStorable? root = null;
		await foreach (var candidate in source.GetRootsAsync())
		{
			root = (IWindowsStorable)candidate;
			break;
		}

		Assert.IsNotNull(root);
		var resolved = (IWindowsStorable)await source.ResolveAsync(root!.Address);

		Assert.IsFalse(root.IsFileSystem);
		StringAssert.StartsWith(root.Id, "winshell-address:v1:");
		Assert.AreEqual(root.Id, resolved.Id);
	}

	private static async Task<IStorableChild?> FindItemAsync(IFolder folder, string name)
	{
		await foreach (var item in folder.GetItemsAsync(StorableType.File))
		{
			if (StringComparer.Ordinal.Equals(item.Name, name))
			{
				return item;
			}
		}

		return null;
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

		using var process = Process.Start(startInfo)
			?? throw new InvalidOperationException("Could not start fsutil.exe.");
		var error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode is not 0)
		{
			throw new IOException($"Could not create the test hard link: {error}");
		}
	}
}
