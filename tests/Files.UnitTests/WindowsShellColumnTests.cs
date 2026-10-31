// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core;
using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Storage;
using Files.Core.Storage.Windows;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for windows shell column behavior.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsShellColumnTests
{
	/// <summary>
	/// Test case: windows folder reports shell columns.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WindowsFolderReportsShellColumns()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ShellColumnTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);

		try
		{
			await using var runtime = new FilesCoreBuilder().AddWindowsStorage(enablePreviews: false, enableArchives: false, enableOperations: false).Build();
			var source = (WindowsStorageSource)runtime.Workspace.Sources.Single();
			var address = new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath);
			var folder = (WindowsFolder)await source.ResolveAsync(address);

			var columnSet = await source.Scheduler.InvokeAsync(() =>
				WindowsShellColumnReader.Read(WindowShellHelpers.TryResolveItem(folder.Locator)!, folder.ParsingName));

			Assert.IsTrue(columnSet.All.Count > 0);
			Assert.IsTrue(columnSet.DefaultVisible.Count > 0);
			Assert.IsTrue(columnSet.All.All(static column => column.PropertyId.Length > 0 && column.DisplayName.Length > 0));
			Assert.IsTrue(columnSet.All.All(static column => Enum.IsDefined(column.Type)));
			Assert.IsTrue(columnSet.All.Any(static column => column.Type is not WindowsShellColumnType.Default));
			Assert.IsTrue(columnSet.DefaultVisible.Any(static column => column.PropertyId.Equals("System.ItemTypeText", StringComparison.Ordinal) && column.HeaderWidthCharacters > 0));
			Assert.AreEqual(34, columnSet.DefaultVisible.Single(static column => column.PropertyId.Equals("System.ItemNameDisplay", StringComparison.Ordinal)).HeaderWidthCharacters);
			CollectionAssert.AreEqual(
				columnSet.All.Where(static column => column.IsVisibleByDefault && !column.IsHidden).Select(static column => column.PropertyId).ToArray(),
				columnSet.DefaultVisible.Select(static column => column.PropertyId).ToArray());
			await using var model = await runtime.Workspace.ResolveAsync(address);
			var metadata = model.Get<IFolderBrowseMetadata>();
			Assert.IsNotNull(metadata);
			var columns = await metadata.GetColumnsAsync();
			Assert.IsNotNull(columns);
			Assert.IsTrue(columns.All.Any(static column => column.PropertyId == "System.ItemNameDisplay"));
			Assert.IsTrue(columns.DefaultVisible.Count > 0);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}
