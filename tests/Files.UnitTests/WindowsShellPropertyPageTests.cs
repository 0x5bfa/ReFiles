// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Windows;

namespace Files.UnitTests;

/// <summary>
/// Verifies Windows Shell property page discovery.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsShellPropertyPageTests
{
	/// <summary>
	/// Verifies that a filesystem file exposes its default and registered property pages without displaying a native property sheet.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task FileSystemFileExposesRegisteredPropertyPages()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.PropertyPageTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var filePath = Path.Combine(directoryPath, "item.txt");
		File.WriteAllText(filePath, "content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var item = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, filePath));
			var reference = new StorableReference(source.SourceId, item.Id, item.Address);
			var service = new WindowsShellAppExtensionService(source);

			var pages = await service.GetPropertyPagesAsync([reference]);

			Assert.IsTrue(pages.Count >= 5);
			Assert.IsTrue(pages.Any(static page => page.IsDefault));
			Assert.IsTrue(pages.Any(static page => !page.IsDefault));
			Assert.IsTrue(pages.Any(static page => !string.IsNullOrWhiteSpace(page.Title)));
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}
