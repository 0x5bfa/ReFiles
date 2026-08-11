// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using Files.Core.Browsing;
using Files.Core.Composition;
using Files.Core.Sessions;
using Files.Core.Storage;

namespace Files.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class WindowsStorageTests
{
	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public async Task MeasuresSystem32CoreEnumeration()
	{
		var directoryPath = Environment.SystemDirectory;
		Assert.IsTrue(Directory.Exists(directoryPath), $"The system directory does not exist: {directoryPath}");

		var totalStart = Stopwatch.GetTimestamp();
		await using var scheduler = new WindowsShellScheduler();
		await using var source = new WindowsStorageSource(scheduler: scheduler);
		var resolveStart = Stopwatch.GetTimestamp();
		var folder = (IFolder)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
		var resolveMilliseconds = Stopwatch.GetElapsedTime(resolveStart).TotalMilliseconds;
		var enumerationStart = Stopwatch.GetTimestamp();
		var firstItemTimestamp = 0L;
		var itemCount = 0;

		await foreach (var item in folder.GetItemsAsync().ConfigureAwait(false))
		{
			itemCount++;
			if (firstItemTimestamp is 0)
			{
				firstItemTimestamp = Stopwatch.GetTimestamp();
			}
		}

		var firstItemMilliseconds = firstItemTimestamp is 0 ? -1 : Stopwatch.GetElapsedTime(enumerationStart, firstItemTimestamp).TotalMilliseconds;
		var enumerationMilliseconds = Stopwatch.GetElapsedTime(enumerationStart).TotalMilliseconds;
		var totalMilliseconds = Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds;
		var measurement = $"System32 Core: path={directoryPath}, items={itemCount}, resolve={resolveMilliseconds:F1} ms, firstItem={firstItemMilliseconds:F1} ms, " +
			$"enumeration={enumerationMilliseconds:F1} ms, total={totalMilliseconds:F1} ms";
		TestContext.WriteLine(measurement);

		Assert.IsTrue(itemCount > 0, "The system directory should contain at least one item.");
	}

	[TestMethod]
	public async Task MeasuresSystem32AppModelNavigation()
	{
		var directoryPath = Environment.SystemDirectory;
		Assert.IsTrue(Directory.Exists(directoryPath), $"The system directory does not exist: {directoryPath}");

		var totalStart = Stopwatch.GetTimestamp();
		await using var runtime = new FilesCoreBuilder()
			.AddWindowsStorage(enablePreviews: false, enableArchives: false)
			.Build();
		var runtimeMilliseconds = Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds;
		var resolveStart = Stopwatch.GetTimestamp();
		var folderModel = await runtime.Workspace.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
		var reference = folderModel.Reference;
		await folderModel.DisposeAsync();
		var resolveMilliseconds = Stopwatch.GetElapsedTime(resolveStart).TotalMilliseconds;
		var navigationStart = Stopwatch.GetTimestamp();
		var window = await runtime.ShellSession.CreateWindowAsync(new FolderLocation(reference));
		var navigationMilliseconds = Stopwatch.GetElapsedTime(navigationStart).TotalMilliseconds;
		var totalMilliseconds = Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds;
		var pane = window.ActiveTab?.ActivePane;
		Assert.IsNotNull(pane);
		var browsePane = pane.Content as BrowsePaneSession;
		Assert.IsNotNull(browsePane);
		var itemCount = browsePane.BrowseSession.Items.Count;
		var measurement = $"System32 AppModel: path={directoryPath}, items={itemCount}, runtime={runtimeMilliseconds:F1} ms, resolve={resolveMilliseconds:F1} ms, " +
			$"navigation={navigationMilliseconds:F1} ms, total={totalMilliseconds:F1} ms";
		TestContext.WriteLine(measurement);

		Assert.IsTrue(itemCount > 0, "The system directory should contain at least one item.");
	}

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
	public async Task LargeFileSystemFolderEnumerationCompletesBeyondTheChannelCapacity()
	{
		const int itemCount = 512;
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.LargeEnumerationTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);

		try
		{
			for (var index = 0; index < itemCount; index++)
			{
				File.WriteAllText(Path.Combine(directoryPath, $"file-{index:D3}.txt"), string.Empty);
			}

			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var folder = (IFolder)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
			var items = new List<IStorableChild>(itemCount);
			await foreach (var item in folder.GetItemsAsync(StorableType.File))
			{
				items.Add(item);
			}

			Assert.AreEqual(itemCount, items.Count);
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
	public async Task RapidlyCanceledColumnReadsLeaveSchedulerUsable()
	{
		var directoryPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		Assert.IsTrue(Directory.Exists(directoryPath), $"The Windows directory '{directoryPath}' does not exist.");

		await using var scheduler = new WindowsShellScheduler();
		await using var source = new WindowsStorageSource(scheduler: scheduler);
		var folder = (WindowsFolder)await source.ResolveAsync(new Files.Core.Storage.StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
		var requests = new List<(CancellationTokenSource Cancellation, Task<WindowsShellColumnSet> Task)>();
		for (var iteration = 0; iteration < 250; iteration++)
		{
			var cancellation = new CancellationTokenSource();
			requests.Add((cancellation, folder.GetColumnsAsync(cancellation.Token)));
			cancellation.Cancel();
		}

		foreach (var request in requests)
		{
			try
			{
				await request.Task;
			}
			catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
			{
			}
			finally
			{
				request.Cancellation.Dispose();
			}
		}

		var columns = await folder.GetColumnsAsync().WaitAsync(TimeSpan.FromSeconds(30));

		Assert.IsTrue(columns.All.Count > 0);
	}

	[TestMethod]
	public async Task DirectResolutionAndEnumerationShareTheSameIdentity()
	{
		var filePath = typeof(WindowsStorageTests).Assembly.Location;
		var directoryPath = Path.GetDirectoryName(filePath)!;
		await using var scheduler = new WindowsShellScheduler();
		await using var source = new WindowsStorageSource(scheduler: scheduler);
		var folder = (IFolder)await source.ResolveAsync(new Files.Core.Storage.StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
		var enumerated = await FindItemAsync(folder, Path.GetFileName(filePath));
		var direct = (IWindowsStorable)await source.ResolveAsync(new Files.Core.Storage.StorageAddress(WindowsStorageSource.FileAddressScheme, filePath));

		Assert.IsNotNull(enumerated);
		Assert.AreEqual(direct.Id, enumerated!.Id);
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
