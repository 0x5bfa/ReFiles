// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Drawing;
using System.IO;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Files.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace Files.UITests;

/// <summary>
/// Verifies native Shell data transfer across the WinUI data-package boundary.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WinUiDataObjectBridgeTests
{
	private const string ExpectedContent = "WinUI Shell bridge content";
	private const string PreferredDropEffectFormat = "Preferred DropEffect";

	/// <summary>
	/// Verifies that a Shell data object survives the WinUI bridge and can be dropped on a native folder target.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task NativeShellDataObjectRoundTripsThroughWinUiDataPackage()
	{
		var rootPath = Path.Combine(Path.GetTempPath(), $"Files.WinUiDataObjectBridgeTests-{Guid.NewGuid():N}");
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
			var sourceItem = Assert.IsInstanceOfType<WindowsStorable>(await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, sourceFilePath)));
			var destinationItem = Assert.IsInstanceOfType<WindowsStorable>(await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, destinationFolderPath)));
			var sourceReference = new StorableReference(source.SourceId, sourceItem.Id, sourceItem.Address);
			var destinationReference = new StorableReference(source.SourceId, destinationItem.Id, destinationItem.Address);
			var dragSource = await source.DragDrop.PrepareDragSourceAsync([sourceReference]);
			var dropTarget = await source.DragDrop.PrepareDropTargetAsync(destinationReference, background: true);
			var dataPackage = new DataPackage();
			var allowedOperations = WinUiDataObjectBridge.Attach(dragSource, dataPackage, 0, WindowsShellDropEffects.Link, deriveMoveFromDelete: false);

			Assert.IsTrue(allowedOperations.HasFlag(DataPackageOperation.Copy));
			var dataObject = WinUiDataObjectBridge.GetDataObject(dataPackage.GetView());
			Assert.IsTrue(TryGetDword(dataObject, PreferredDropEffectFormat, out var preferredEffect));
			Assert.AreEqual((uint)WindowsShellDropEffects.Link, preferredEffect);
			Assert.IsTrue(dropTarget.TryCreateSession(dataObject, 0, out var session));
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

	private static unsafe bool TryGetDword(IDataObject dataObject, string formatName, out uint value)
	{
		value = 0;
		var clipboardFormat = PInvoke.RegisterClipboardFormat(formatName);
		if (clipboardFormat is 0)
		{
			return false;
		}

		var format = default(FORMATETC);
		format.cfFormat = checked((ushort)clipboardFormat);
		format.dwAspect = (uint)DVASPECT.DVASPECT_CONTENT;
		format.lindex = -1;
		format.tymed = (uint)TYMED.TYMED_HGLOBAL;
		if (dataObject.GetData(in format, out var medium).Failed)
		{
			return false;
		}

		try
		{
			if (medium.tymed is not TYMED.TYMED_HGLOBAL || medium.u.hGlobal.IsNull || PInvoke.GlobalSize(medium.u.hGlobal) < sizeof(uint))
			{
				return false;
			}

			var buffer = PInvoke.GlobalLock(medium.u.hGlobal);
			if (buffer is null)
			{
				return false;
			}

			try
			{
				value = *(uint*)buffer;

				return true;
			}
			finally
			{
				_ = PInvoke.GlobalUnlock(medium.u.hGlobal);
			}
		}
		finally
		{
			PInvoke.ReleaseStgMedium(ref medium);
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
