// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Drawing;
using System.IO;
using Files.Core.Storage;
using Files.Infrastructure;
using Files.Core.Storage.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Windows.ApplicationModel.DataTransfer;

namespace Files.UITests;

/// <summary>
/// Verifies native Shell data transfer across the WinUI data-package boundary.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WinUIDataObjectBridgeTests
{
	private const string ExpectedContent = "WinUI Shell bridge content";

	/// <summary>
	/// Verifies that a Shell data object survives the WinUI bridge and can be dropped on a native folder target.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task NativeShellDataObjectRoundTripsThroughWinUIDataPackage()
	{
		var rootPath = Path.Combine(Path.GetTempPath(), $"Files.WinUIDataObjectBridgeTests-{Guid.NewGuid():N}");
		var sourceFolderPath = Path.Combine(rootPath, "source");
		var destinationFolderPath = Path.Combine(rootPath, "destination");
		var sourceFilePath = Path.Combine(sourceFolderPath, "source.txt");
		var destinationFilePath = Path.Combine(destinationFolderPath, "source.txt");
		Directory.CreateDirectory(sourceFolderPath);
		Directory.CreateDirectory(destinationFolderPath);
		await File.WriteAllTextAsync(sourceFilePath, ExpectedContent);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var sourceItem = Assert.IsInstanceOfType<IWindowsStorable>(await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, sourceFilePath)));
			var destinationItem = Assert.IsInstanceOfType<IWindowsStorable>(await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, destinationFolderPath)));
			var sourceReference = new StorableReference(source.SourceId, sourceItem.Id, sourceItem.Address);
			var destinationReference = new StorableReference(source.SourceId, destinationItem.Id, destinationItem.Address);
			var dragSource = await source.DragDrop.PrepareDragSourceAsync((Files.Core.Storage.StorableReference[])[sourceReference]);
			IWindowsShellDropTarget dropTarget = await source.DragDrop.PrepareDropTargetAsync(destinationReference, background: true);
			Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
			var dataPackage = new DataPackage();
			var allowedOperations = WinUIDataObjectBridge.Attach(dragSource, dataPackage, 0, WindowsShellDropEffects.Link, deriveMoveFromDelete: false);

			Assert.IsTrue(allowedOperations.HasFlag(DataPackageOperation.Copy));
			Assert.AreEqual(DataPackageOperation.Link, dataPackage.RequestedOperation);
			Assert.IsTrue(dataPackage.GetView().Contains(StandardDataFormats.StorageItems), "The bridged drag package was not projected as StorageItems.");
			var defaultDataPackage = new DataPackage();
			WinUIDataObjectBridge.Attach(dragSource, defaultDataPackage, 0, WindowsShellDropEffects.None, deriveMoveFromDelete: false);
			Assert.AreEqual(DataPackageOperation.None, defaultDataPackage.RequestedOperation);
			var dataTransfer = WinUIDataObjectBridge.GetDataObject(source.DragDrop, dataPackage.GetView());
			Assert.IsTrue(dropTarget.TryCreateSession(dataTransfer, 0, out var session));
			Assert.IsNotNull(session);
			using (session)
			{
				Assert.IsTrue(session.TryDragEnter(WindowsShellDragDropModifiers.LeftButton, Point.Empty, WindowsShellDropEffects.Copy, out var enterEffect));
				Assert.AreEqual(WindowsShellDropEffects.Copy, enterEffect);
				Assert.AreEqual(WindowsShellDropEffects.Copy, session.DragOver(WindowsShellDragDropModifiers.LeftButton, Point.Empty, WindowsShellDropEffects.Copy));
				Assert.AreEqual(WindowsShellDropEffects.Copy, session.Drop(WindowsShellDragDropModifiers.LeftButton, Point.Empty, WindowsShellDropEffects.Copy));
			}

			Assert.IsTrue(await WaitForFileContentAsync(destinationFilePath, ExpectedContent, TimeSpan.FromSeconds(10)),
				"The bridged Shell data object was not copied with the expected content by the native drop target.");
		}
		finally
		{
			await DeleteTestRootAsync(rootPath);
		}
	}

	private static async Task<bool> WaitForFileContentAsync(string path, string expectedContent, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				if (await File.ReadAllTextAsync(path) == expectedContent)
				{
					return true;
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}

			await Task.Delay(50);
		}

		return false;
	}

	private static async Task DeleteTestRootAsync(string rootPath)
	{
		for (var attempt = 0; attempt < 20; attempt++)
		{
			try
			{
				if (!Directory.Exists(rootPath))
				{
					return;
				}

				Directory.Delete(rootPath, recursive: true);

				return;
			}
			catch (IOException) when (attempt < 19)
			{
			}
			catch (UnauthorizedAccessException) when (attempt < 19)
			{
			}

			await Task.Delay(50);
		}
	}
}
