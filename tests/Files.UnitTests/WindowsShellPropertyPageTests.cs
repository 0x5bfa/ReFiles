// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Models;
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
	/// <summary>General-page icons honor composed capabilities and remain absent when no thumbnail capability is registered.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task GeneralPropertiesUseTheComposedThumbnailCapability()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.PropertyIconTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		try
		{
			await using var source = new WindowsStorageSource();
			var item = await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
			var expected = new ThumbnailResult(new byte[] { 0, 0, 0, 255 }, "application/octet-stream", false, ThumbnailContentFormat.Bgra8, 1, 1);
			var composed = new TestThumbnailSource { Handler = (_, _) => ValueTask.FromResult<ThumbnailResult?>(expected) };
			var registry = new CapabilityBuilder().Add<IThumbnailSource>(new WindowsThumbnailSourceFactory())
				.AddWrapper<IThumbnailSource>(new DelegateCapabilityWrapper<IThumbnailSource>((_, _) => composed)).Build();
			await using var model = new StorableModelFactory(registry).Create(source, item);
			var service = new WindowsShellPropertiesService(source);
			var result = await service.GetGeneralPropertiesAsync(model);
			Assert.AreSame(expected, result.Icon);
			Assert.AreEqual(1, composed.CallCount);
			Assert.AreEqual(ThumbnailMode.Icon, composed.Requests[0].Mode);
			Assert.AreEqual(48, composed.Requests[0].RequestedPixelSize);

			await using var withoutThumbnails = new StorableModelFactory(CapabilityRegistry.Empty).Create(source, item);
			var unsupported = await service.GetGeneralPropertiesAsync(withoutThumbnails);
			Assert.IsNull(unsupported.Icon);
			Assert.AreEqual(1, composed.CallCount);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Verifies that Shell property-page stock icons can be converted to independent PNG data.
	/// </summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task PropertyPageStockIconsCanBeLoaded()
	{
		IWindowsShellIconResources resources = new WindowsShellIconResources();
		Assert.IsFalse((await resources.GetElevationShieldIconAsync()).IsEmpty);
		Assert.IsFalse((await resources.GetFolderIconAsync()).IsEmpty);
	}

	/// <summary>
	/// Verifies that Shell resource icons retain the requested physical pixel size.
	/// </summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task ShellResourceIconUsesRequestedPixelSize()
	{
		const int requestedSize = 24;
		var shellLibrary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "shell32.dll");
		IWindowsShellIconResources resources = new WindowsShellIconResources();
		var icon = await resources.GetResourceIconAsync(shellLibrary, 0, requestedSize);
		Assert.IsTrue(icon.Length >= 24);
		Assert.AreEqual(requestedSize, BinaryPrimitives.ReadInt32BigEndian(icon.Span.Slice(16, sizeof(int))));
		Assert.AreEqual(requestedSize, BinaryPrimitives.ReadInt32BigEndian(icon.Span.Slice(20, sizeof(int))));
	}

	/// <summary>
	/// Verifies that folder-picture changes are persisted through the public Shell customization API.
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
			WindowsShellFolderCustomizationService.Apply(directoryPath, string.Empty, false, false, picturePath, true, string.Empty, 0, false);
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var item = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
			var reference = new StorableReference(source.SourceId, item.Id, item.Address);
			var service = new WindowsShellPropertiesService(source);
			var data = await service.GetPropertySheetDataAsync([reference]);

			Assert.IsNotNull(data?.Customization);
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
		var service = new WindowsShellPropertiesService(source);

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
		var service = new WindowsShellPropertiesService(source);

		var data = await service.GetPropertySheetDataAsync([reference]);

		Assert.IsNotNull(data);
		Assert.IsFalse(data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Tools));
		Assert.IsFalse(data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.PreviousVersions));
		Assert.IsTrue(data.Pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Hardware));
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
		var service = new WindowsShellPropertiesService(source);

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
			var service = new WindowsShellPropertiesService(source);

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
	/// Verifies that property-page data is read independently and that multi-selection details remain managed by the presentation model.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task PropertyPageDataCanBeLoadedIndependently()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.PropertyPageTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var firstPath = Path.Combine(directoryPath, "first.txt");
		var secondPath = Path.Combine(directoryPath, "second.txt");
		File.WriteAllText(firstPath, "first");
		File.WriteAllText(secondPath, "second");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var first = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, firstPath));
			var second = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, secondPath));
			var firstReference = new StorableReference(source.SourceId, first.Id, first.Address);
			var secondReference = new StorableReference(source.SourceId, second.Id, second.Address);
			var service = new WindowsShellPropertiesService(source);

			var security = await service.GetPropertyPageDataAsync([firstReference], WindowsShellPropertyPageKind.Security);
			var details = await service.GetPropertyPageDataAsync([firstReference], WindowsShellPropertyPageKind.Details);
			var multiSelectionDetails = await service.GetPropertyPageDataAsync([firstReference, secondReference], WindowsShellPropertyPageKind.Details);

			Assert.IsNotNull(security?.Security);
			CollectionAssert.AreEqual(new[] { WindowsShellPropertyPageKind.Security }, security.Pages.Select(static page => page.Kind).ToArray());
			Assert.IsEmpty(security.Details);
			Assert.IsNotNull(details);
			CollectionAssert.AreEqual(new[] { WindowsShellPropertyPageKind.Details }, details.Pages.Select(static page => page.Kind).ToArray());
			Assert.IsNotEmpty(details.Details);
			Assert.IsNotNull(multiSelectionDetails);
			Assert.IsEmpty(multiSelectionDetails.Details);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Verifies that classic context-menu targets copy same-parent Shell item ID lists and reject mixed parents.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ClassicContextMenuTargetRequiresCommonParent()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.PropertyPageTests-{Guid.NewGuid():N}");
		var otherDirectoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.PropertyPageTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		Directory.CreateDirectory(otherDirectoryPath);
		var firstPath = Path.Combine(directoryPath, "first.txt");
		var secondPath = Path.Combine(directoryPath, "second.txt");
		var otherPath = Path.Combine(otherDirectoryPath, "other.txt");
		File.WriteAllText(firstPath, "first");
		File.WriteAllText(secondPath, "second");
		File.WriteAllText(otherPath, "other");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var first = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, firstPath));
			var second = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, secondPath));
			var other = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, otherPath));
			var firstReference = new StorableReference(source.SourceId, first.Id, first.Address);
			var secondReference = new StorableReference(source.SourceId, second.Id, second.Address);
			var otherReference = new StorableReference(source.SourceId, other.Id, other.Address);
			var sameParent = await WindowsShellContextMenuTarget.CreateAsync(source, [firstReference, secondReference]);
			var mixedParents = await WindowsShellContextMenuTarget.CreateAsync(source, [firstReference, otherReference]);

			Assert.IsNotNull(sameParent);
			Assert.HasCount(2, sameParent.AbsolutePidls);
			Assert.IsTrue(sameParent.AbsolutePidls.All(static pidl => !pidl.IsEmpty));
			Assert.IsNull(mixedParents);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
			Directory.Delete(otherDirectoryPath, recursive: true);
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
			var service = new WindowsShellPropertiesService(source);

			var pages = await service.GetPropertyPagesAsync([reference]);

			var pageKinds = pages.Select(static page => page.Kind).ToArray();
			CollectionAssert.AreEqual(
				new[]
				{
					WindowsShellPropertyPageKind.General,
					WindowsShellPropertyPageKind.Security,
					WindowsShellPropertyPageKind.PreviousVersions,
					WindowsShellPropertyPageKind.Customize,
				},
				pageKinds.Where(static kind => kind is not WindowsShellPropertyPageKind.Sharing).ToArray());
			Assert.IsTrue(pageKinds.Count(static kind => kind is WindowsShellPropertyPageKind.Sharing) <= 1);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}
