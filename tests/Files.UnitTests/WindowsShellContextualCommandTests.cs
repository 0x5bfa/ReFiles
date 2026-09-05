// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>Verifies contextual Windows Shell command discovery.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsShellContextualCommandTests
{
	/// <summary>Verifies that mounted-disc commands are exposed only for a single ISO selection.</summary>
	[TestMethod]
	public async Task IsoSelectionsExposeMountAndBurnCommandsOnlyForSingleSelection()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ContextualCommandTests-{Guid.NewGuid():N}");
		var isoPath = Path.Combine(directoryPath, "sample.iso");
		var secondIsoPath = Path.Combine(directoryPath, "second.iso");
		var textPath = Path.Combine(directoryPath, "sample.txt");
		Directory.CreateDirectory(directoryPath);
		await File.WriteAllBytesAsync(isoPath, []);
		await File.WriteAllBytesAsync(secondIsoPath, []);
		await File.WriteAllTextAsync(textPath, "content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var reference = await ResolveReferenceAsync(source, isoPath);
			var secondIso = await ResolveReferenceAsync(source, secondIsoPath);
			var text = await ResolveReferenceAsync(source, textPath);
			var service = new WindowsShellContextualCommandService(source);

			var commands = await service.GetCommandsAsync(null, [reference], 0);
			var ids = commands.Select(static command => command.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var multipleIsoIds = (await service.GetCommandsAsync(null, [reference, secondIso], 0)).Select(static command => command.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var mixedSelectionIds = (await service.GetCommandsAsync(null, [reference, text], 0)).Select(static command => command.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

			Assert.IsTrue(ids.Contains(WindowsShellContextualCommandIds.Mount), $"Discovered commands: {string.Join(", ", ids)}");
			Assert.IsTrue(ids.Contains(WindowsShellContextualCommandIds.BurnDiscImage), $"Discovered commands: {string.Join(", ", ids)}");
			Assert.IsFalse(multipleIsoIds.Contains(WindowsShellContextualCommandIds.Mount), $"Multiple ISO commands: {string.Join(", ", multipleIsoIds)}");
			Assert.IsFalse(multipleIsoIds.Contains(WindowsShellContextualCommandIds.BurnDiscImage), $"Multiple ISO commands: {string.Join(", ", multipleIsoIds)}");
			Assert.IsFalse(mixedSelectionIds.Contains(WindowsShellContextualCommandIds.Mount), $"Mixed selection commands: {string.Join(", ", mixedSelectionIds)}");
			Assert.IsFalse(mixedSelectionIds.Contains(WindowsShellContextualCommandIds.BurnDiscImage), $"Mixed selection commands: {string.Join(", ", mixedSelectionIds)}");

			var recycleBin = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.ShellAddressScheme, "shell:::{645FF040-5081-101B-9F08-00AA002F954E}"));
			var recycleBinLocation = new StorableReference(source.SourceId, recycleBin.Id, recycleBin.Address);
			var recycleBinIds = (await service.GetCommandsAsync(recycleBinLocation, [reference], 0)).Select(static command => command.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

			Assert.IsFalse(recycleBinIds.Contains(WindowsShellContextualCommandIds.Mount), $"Recycle Bin commands: {string.Join(", ", recycleBinIds)}");
			Assert.IsFalse(recycleBinIds.Contains(WindowsShellContextualCommandIds.BurnDiscImage), $"Recycle Bin commands: {string.Join(", ", recycleBinIds)}");
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>Verifies that an image selection exposes the desktop background command registered by the Shell.</summary>
	[TestMethod]
	public async Task ImageSelectionExposesSetDesktopBackgroundCommand()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ContextualCommandTests-{Guid.NewGuid():N}");
		var imagePath = Path.Combine(directoryPath, "sample.jpg");
		Directory.CreateDirectory(directoryPath);
		await File.WriteAllBytesAsync(imagePath, []);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var image = await ResolveReferenceAsync(source, imagePath);
			var service = new WindowsShellContextualCommandService(source);

			var command = (await service.GetCommandsAsync(null, [image], 0))
				.Single(static command => command.Id.Equals(WindowsShellContextualCommandIds.SetDesktopBackground, StringComparison.OrdinalIgnoreCase));

			Assert.AreEqual(WindowsShellContextualCommandScope.Selection, command.Scope);
			Assert.IsTrue(command.IsEnabled);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>Verifies that the Recycle Bin location exposes its location commands even when no item is selected.</summary>
	[TestMethod]
	public async Task RecycleBinLocationExposesLocationCommands()
	{
		await using var scheduler = new WindowsShellScheduler();
		await using var source = new WindowsStorageSource(scheduler: scheduler);
		var item = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.ShellAddressScheme, "shell:::{645FF040-5081-101B-9F08-00AA002F954E}"));
		var location = new StorableReference(source.SourceId, item.Id, item.Address);
		var service = new WindowsShellContextualCommandService(source);

		var commands = await service.GetCommandsAsync(location, [], 0);
		var commandIds = commands.Select(static command => command.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

		Assert.IsTrue(commandIds.Contains(WindowsShellContextualCommandIds.EmptyRecycleBin));
		Assert.IsTrue(commandIds.Contains(WindowsShellContextualCommandIds.RestoreAllRecycleBinItems), $"Discovered commands: {string.Join(", ", commandIds)}");
	}

	/// <summary>Verifies the secondary contextual commands exposed for file-system folders and files.</summary>
	[TestMethod]
	public async Task FileSystemItemsExposeSecondaryCommands()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ContextualCommandTests-{Guid.NewGuid():N}");
		var childFolderPath = Path.Combine(directoryPath, "Folder");
		var filePath = Path.Combine(directoryPath, "File.txt");
		Directory.CreateDirectory(childFolderPath);
		await File.WriteAllTextAsync(filePath, "content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var location = await ResolveReferenceAsync(source, directoryPath);
			var folder = await ResolveReferenceAsync(source, childFolderPath);
			var file = await ResolveReferenceAsync(source, filePath);
			var service = new WindowsShellContextualCommandService(source);

			var locationIds = (await service.GetCommandsAsync(location, [], 0)).Select(static command => command.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var folderIds = (await service.GetCommandsAsync(location, [folder], 0)).Select(static command => command.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var fileIds = (await service.GetCommandsAsync(location, [file], 0)).Select(static command => command.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

			Assert.IsTrue(locationIds.Contains(WindowsShellContextualCommandIds.PinToQuickAccess), $"Location commands: {string.Join(", ", locationIds)}");
			Assert.IsTrue(folderIds.Contains(WindowsShellContextualCommandIds.CompressToZip), $"Folder commands: {string.Join(", ", folderIds)}");
			Assert.IsTrue(folderIds.Contains(WindowsShellContextualCommandIds.PinToQuickAccess), $"Folder commands: {string.Join(", ", folderIds)}");
			Assert.IsTrue(folderIds.Contains(WindowsShellContextualCommandIds.CopyAsPath), $"Folder commands: {string.Join(", ", folderIds)}");
			Assert.IsTrue(fileIds.Contains(WindowsShellContextualCommandIds.CompressToZip), $"File commands: {string.Join(", ", fileIds)}");
			Assert.IsTrue(fileIds.Contains(WindowsShellContextualCommandIds.AddToFavorites), $"File commands: {string.Join(", ", fileIds)}");
			Assert.IsTrue(fileIds.Contains(WindowsShellContextualCommandIds.CopyAsPath), $"File commands: {string.Join(", ", fileIds)}");
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>Verifies that the registered ZIP backend accepts a file-system selection.</summary>
	[TestMethod]
	public async Task CompressToZipCommandCanBeInvoked()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ContextualCommandTests-{Guid.NewGuid():N}");
		var filePath = Path.Combine(directoryPath, "File.txt");
		Directory.CreateDirectory(directoryPath);
		await File.WriteAllTextAsync(filePath, "content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var file = await ResolveReferenceAsync(source, filePath);
			var service = new WindowsShellContextualCommandService(source);
			var command = (await service.GetCommandsAsync(null, [file], 0)).Single(static command => command.Id.Equals(WindowsShellContextualCommandIds.CompressToZip, StringComparison.OrdinalIgnoreCase));

			var invoked = await service.InvokeAsync(null, [file], command, new WindowsShellInvocationContext((nint)1, directoryPath));

			Assert.IsTrue(invoked);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	private static async Task<StorableReference> ResolveReferenceAsync(WindowsStorageSource source, string path)
	{
		var item = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, path));

		return new StorableReference(source.SourceId, item.Id, item.Address);
	}
}
