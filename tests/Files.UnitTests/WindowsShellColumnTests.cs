// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.ViewSettings;
using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for windows shell column behavior.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsShellColumnTests
{
	/// <summary>
	/// Test case: windows shell view modes map to supported browse layouts.
	/// </summary>
	[TestMethod]
	public void ShellViewModesMapToBrowseLayouts()
	{
		Assert.AreEqual(ViewLayoutMode.Details, WindowsShellColumnReader.MapLayoutMode(4));
		Assert.AreEqual(ViewLayoutMode.List, WindowsShellColumnReader.MapLayoutMode(3));
		Assert.AreEqual(ViewLayoutMode.List, WindowsShellColumnReader.MapLayoutMode(2));
		Assert.AreEqual(ViewLayoutMode.Cards, WindowsShellColumnReader.MapLayoutMode(6));
		Assert.AreEqual(ViewLayoutMode.Cards, WindowsShellColumnReader.MapLayoutMode(8));
		Assert.AreEqual(ViewLayoutMode.Grid, WindowsShellColumnReader.MapLayoutMode(1));
		Assert.AreEqual(ViewLayoutMode.Grid, WindowsShellColumnReader.MapLayoutMode(5));
		Assert.AreEqual(ViewLayoutMode.Grid, WindowsShellColumnReader.MapLayoutMode(7));
		Assert.IsNull(WindowsShellColumnReader.MapLayoutMode(uint.MaxValue));
		Assert.IsNull(WindowsShellColumnReader.MapLayoutMode(0));
	}

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
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var folder = (WindowsFolder)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));

			var columnSet = await folder.GetColumnsAsync();

			Assert.IsTrue(columnSet.All.Count > 0);
			Assert.IsTrue(columnSet.DefaultVisible.Count > 0);
			Assert.IsTrue(columnSet.All.All(static column => column.PropertyId.Length > 0 && column.DisplayName.Length > 0));
			Assert.IsTrue(columnSet.All.All(static column => Enum.IsDefined(column.Type)));
			Assert.IsTrue(columnSet.All.Any(static column => column.Type is not WindowsShellColumnType.Default));
			Assert.IsTrue(columnSet.DefaultVisible.Any(static column => column.PropertyId.Equals("System.ItemTypeText", StringComparison.Ordinal) && column.HeaderWidthCharacters > 0));
			Assert.AreEqual(34, columnSet.DefaultVisible.Single(static column => column.PropertyId.Equals("System.ItemNameDisplay", StringComparison.Ordinal)).HeaderWidthCharacters);
			Assert.AreEqual(ViewLayoutMode.Details, columnSet.DefaultLayoutMode);
			CollectionAssert.AreEqual(
				columnSet.All.Where(static column => column.IsVisibleByDefault && !column.IsHidden).Select(static column => column.PropertyId).ToArray(),
				columnSet.DefaultVisible.Select(static column => column.PropertyId).ToArray());
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}
