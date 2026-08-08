// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Windows;

namespace Files.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class WindowsShellColumnTests
{
	[TestMethod]
	public async Task WindowsFolderReportsShellColumns()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ShellColumnTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var folder = (WindowsFolder)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));

			var columnSet = await folder.GetColumnsAsync();

			Assert.IsTrue(columnSet.All.Count > 0);
			Assert.IsTrue(columnSet.DefaultVisible.Count > 0);
			Assert.IsTrue(columnSet.All.All(static column => column.PropertyId.Length > 0 && column.DisplayName.Length > 0));
			Assert.IsTrue(columnSet.All.All(static column => Enum.IsDefined(column.Type)));
			Assert.IsTrue(columnSet.All.Any(static column => column.Type is not WindowsShellColumnType.Default));
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}
