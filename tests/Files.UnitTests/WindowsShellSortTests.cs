// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Composition;
using Files.Core.Sessions;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for Windows Shell ordering behavior.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsShellSortTests
{
	/// <summary>
	/// Test case: browse session uses the Shell's natural name ordering for Windows folders.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task BrowseSessionUsesShellNaturalNameOrder()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.SortTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		Directory.CreateDirectory(Path.Combine(directoryPath, "item-20"));
		File.WriteAllBytes(Path.Combine(directoryPath, "item-10.bin"), [10]);
		File.WriteAllBytes(Path.Combine(directoryPath, "item-2.bin"), [2]);

		try
		{
			await using var runtime = new FilesCoreBuilder().AddWindowsStorage(enablePreviews: false, enableArchives: false).Build();
			var folderModel = await runtime.Workspace.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
			var reference = folderModel.Reference;
			await folderModel.DisposeAsync();
			var window = await runtime.ShellSession.CreateWindowAsync(new FolderLocation(reference));
			var pane = window.ActiveTab?.ActivePane?.Content as BrowsePaneSession;
			Assert.IsNotNull(pane);

			CollectionAssert.AreEqual(new[] { "item-20", "item-2.bin", "item-10.bin" }, pane.BrowseSession.Items.Select(static item => item.Name).ToArray());

			await pane.BrowseSession.UpdateViewSettingsAsync(new BrowseViewSettings(sortPropertyId: "name", sortDirection: ViewSortDirection.Descending));

			CollectionAssert.AreEqual(new[] { "item-20", "item-10.bin", "item-2.bin" }, pane.BrowseSession.Items.Select(static item => item.Name).ToArray());
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}
