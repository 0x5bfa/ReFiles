// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>
/// Verifies Windows Shell New menu behavior.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsShellNewMenuTests
{
	/// <summary>
	/// Verifies that New menu icons are copied into independent PNG data.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NewMenuItemsIncludeShellIcons()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.NewMenuTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			var menu = new WindowsShellNewMenu(scheduler);
			var items = await menu.GetItemsAsync(directoryPath);
			var iconData = items.Select(static item => item.IconData).FirstOrDefault(static data => !data.IsEmpty);

			Assert.IsNotEmpty(items);
			Assert.IsFalse(iconData.IsEmpty);
			CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, iconData.Span[..4].ToArray());
		}
		finally
		{
			Directory.Delete(directoryPath);
		}
	}
}
