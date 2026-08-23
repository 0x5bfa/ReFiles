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
	/// Verifies that Shell property-page stock icons can be converted to independent PNG data.
	/// </summary>
	[TestMethod]
	public void PropertyPageStockIconsCanBeLoaded()
	{
		Assert.IsFalse(WindowsShellIconProvider.GetElevationShieldIcon().IsEmpty);
		Assert.IsFalse(WindowsShellIconProvider.GetFolderIcon().IsEmpty);
	}

	/// <summary>
	/// Verifies that folder-template and picture changes are persisted through the Shell customization APIs.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task FolderCustomizationCanBeApplied()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.PropertyPageTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var picturePath = Path.Combine(directoryPath, "folder.png");
		File.WriteAllBytes(picturePath, [0x89, 0x50, 0x4E, 0x47]);

		try
		{
			WindowsShellFolderCustomizationService.Apply(directoryPath, "Pictures", true, false, picturePath, true, string.Empty, 0, false);
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var item = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
			var reference = new StorableReference(source.SourceId, item.Id, item.Address);
			var service = new WindowsShellAppExtensionService(source);
			var data = await service.GetPropertySheetDataAsync([reference]);

			Assert.IsNotNull(data?.Customization);
			Assert.AreEqual("Pictures", data.Customization.FolderKind, true);
			Assert.AreEqual(picturePath, data.Customization.PicturePath, true);
		}
		finally
		{
			foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
			{
				File.SetAttributes(filePath, FileAttributes.Normal);
			}

			File.SetAttributes(directoryPath, FileAttributes.Normal);
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Verifies that a local volume root exposes Explorer-compatible drive pages and native data.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task LocalDriveExposesNativeDrivePages()
	{
		var root = Path.GetPathRoot(Environment.SystemDirectory)!;
		await using var scheduler = new WindowsShellScheduler();
		await using var source = new WindowsStorageSource(scheduler: scheduler);
		var item = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, root));
		var reference = new StorableReference(source.SourceId, item.Id, item.Address);
		var service = new WindowsShellAppExtensionService(source);

		var data = await service.GetPropertySheetDataAsync([reference]);

		Assert.IsNotNull(data);
		Assert.IsNotNull(data.Drive);
		Assert.AreEqual(root, data.Drive.RootPath, true);
		Assert.IsTrue(data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Tools));
		Assert.IsTrue(WindowsShellStorageSettingsService.SupportsDriveUsage(root));
		if (data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Hardware))
		{
			Assert.IsNotEmpty(data.HardwareDevices);
			Assert.IsTrue(data.HardwareDevices.All(static device => !device.IconData.IsEmpty));
		}

		if (data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Quota))
		{
			Assert.IsNotNull(data.Quota);
		}
	}

	/// <summary>
	/// Verifies that a mounted optical image uses Explorer's optical-volume page set.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task MountedOpticalImageUsesOpticalDrivePages()
	{
		var drive = DriveInfo.GetDrives().FirstOrDefault(static drive => drive.DriveType is DriveType.CDRom && drive.IsReady);
		if (drive is null)
		{
			return;
		}

		await using var scheduler = new WindowsShellScheduler();
		await using var source = new WindowsStorageSource(scheduler: scheduler);
		var item = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, drive.RootDirectory.FullName));
		var reference = new StorableReference(source.SourceId, item.Id, item.Address);
		var service = new WindowsShellAppExtensionService(source);

		var data = await service.GetPropertySheetDataAsync([reference]);

		Assert.IsNotNull(data);
		Assert.IsFalse(data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Tools));
		Assert.IsFalse(data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.PreviousVersions));
		Assert.IsTrue(data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Hardware));
		Assert.IsTrue(data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Sharing));
		Assert.IsTrue(data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Customize));
		Assert.IsNotNull(data.Customization);
		Assert.IsFalse(WindowsShellStorageSettingsService.SupportsDriveUsage(drive.RootDirectory.FullName));
	}

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
		Assert.IsTrue(data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Compatibility));
		Assert.IsNotNull(data.Compatibility);
		Assert.AreEqual(filePath, data.Compatibility.ExecutablePath, true);
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
		var filePath = Path.Combine(directoryPath, "a.mp3");
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
		Assert.IsTrue(data.Security.Principals.Any(static principal => !principal.IconData.IsEmpty));
		Assert.IsNotEmpty(data.Details);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Verifies that a customizable local folder exposes only the applicable folder property pages.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task FileSystemFolderExposesApplicableNativePropertyPages()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.PropertyPageTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var item = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
			var reference = new StorableReference(source.SourceId, item.Id, item.Address);
			var service = new WindowsShellAppExtensionService(source);

			var pages = await service.GetPropertyPagesAsync([reference]);

			CollectionAssert.AreEqual(
				new[]
				{
					WindowsShellPropertyPageKind.General,
					WindowsShellPropertyPageKind.Sharing,
					WindowsShellPropertyPageKind.Security,
					WindowsShellPropertyPageKind.PreviousVersions,
					WindowsShellPropertyPageKind.Customize,
				},
				pages.Select(static page => page.Kind).ToArray());
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}
