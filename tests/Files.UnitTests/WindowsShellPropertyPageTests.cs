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
	/// Verifies that an Authenticode-signed system file exposes signer data without opening CryptUI.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SignedSystemFileExposesSignatureData()
	{
		var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
		Assert.IsTrue(File.Exists(filePath));
		await using var scheduler = new WindowsShellScheduler();
		await using var source = new WindowsStorageSource(scheduler: scheduler);
		var item = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, filePath));
		var reference = new StorableReference(source.SourceId, item.Id, item.Address);
		var service = new WindowsShellAppExtensionService(source);

		var data = await service.GetPropertySheetDataAsync([reference]);

		Assert.IsNotNull(data);
		Assert.IsTrue(data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.DigitalSignatures));
		var signatures = data.EmbeddedSignatures.Concat(data.CatalogSignatures).ToArray();
		Assert.IsNotEmpty(signatures);
		Assert.IsTrue(signatures.Any(static signature => !string.IsNullOrWhiteSpace(signature.Signer)));
	}

	/// <summary>
	/// Verifies that a filesystem file exposes the ReFiles native property pages without loading property-sheet providers.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task FileSystemFileExposesNativePropertyPages()
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

			CollectionAssert.AreEqual(
				new[]
				{
					WindowsShellPropertyPageKind.General,
					WindowsShellPropertyPageKind.Security,
					WindowsShellPropertyPageKind.Details,
					WindowsShellPropertyPageKind.PreviousVersions,
				},
				pages.Select(static page => page.Kind).ToArray());
			Assert.IsTrue(pages.Single(static page => page.Kind is WindowsShellPropertyPageKind.General).IsDefault);
			Assert.IsTrue(pages.Where(static page => page.Kind is not WindowsShellPropertyPageKind.General).All(static page => !page.IsDefault));

			var data = await service.GetPropertySheetDataAsync([reference]);

			Assert.IsNotNull(data);
			Assert.IsNotNull(data.Security);
			Assert.AreEqual(filePath, data.Security.ObjectPath);
			Assert.IsNotEmpty(data.Details);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}
