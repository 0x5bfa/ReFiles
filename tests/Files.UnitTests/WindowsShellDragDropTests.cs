// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Drawing;
using System.IO.Compression;
using Files.Core.Storage;
using Files.Core.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;

namespace Files.UnitTests;

/// <summary>
/// Contains integration tests for native Windows Shell data transfer.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsShellDragDropTests
{
	private const string PasteSucceededFormat = "Paste Succeeded";
	private const string PerformedDropEffectFormat = "Performed DropEffect";

	/// <summary>
	/// Verifies that native source capabilities are masked and interpreted according to the initiating Shell surface.
	/// </summary>
	[TestMethod]
	public void NativeDragSourceMapsShellCapabilitiesBySurfacePolicy()
	{
		var directTransferCapabilities = SFGAO_FLAGS.SFGAO_CANCOPY | SFGAO_FLAGS.SFGAO_CANMOVE | SFGAO_FLAGS.SFGAO_CANLINK;
		Assert.AreEqual(directTransferCapabilities | SFGAO_FLAGS.SFGAO_CANDELETE, WindowsShellDragSource.GetRequestedAttributes(deriveMoveFromDelete: true));
		Assert.AreEqual(directTransferCapabilities, WindowsShellDragSource.GetRequestedAttributes(deriveMoveFromDelete: false));

		var deleteCapability = SFGAO_FLAGS.SFGAO_CANDELETE;
		Assert.AreEqual(WindowsShellDropEffects.Move, WindowsShellDragSource.MapAllowedEffects(deleteCapability, deriveMoveFromDelete: true));
		Assert.AreEqual(WindowsShellDropEffects.None, WindowsShellDragSource.MapAllowedEffects(deleteCapability, deriveMoveFromDelete: false));
		Assert.AreEqual(WindowsShellDropEffects.None, WindowsShellDragSource.MapAllowedEffects(default, deriveMoveFromDelete: true));

		var transferAndNonTransferCapabilities = SFGAO_FLAGS.SFGAO_CANCOPY | SFGAO_FLAGS.SFGAO_CANMOVE | SFGAO_FLAGS.SFGAO_CANLINK | SFGAO_FLAGS.SFGAO_FOLDER;
		Assert.AreEqual(WindowsShellDropEffects.Copy | WindowsShellDropEffects.Move | WindowsShellDropEffects.Link,
			WindowsShellDragSource.MapAllowedEffects(transferAndNonTransferCapabilities, deriveMoveFromDelete: false));
	}

	/// <summary>
	/// Test case: a Shell folder background drop target copies a native Shell data object.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NativeDropSessionCopiesShellDataObject()
	{
		var rootPath = CreateTestRoot("BackgroundCopy");
		var sourceFolderPath = Path.Combine(rootPath, "source");
		var destinationFolderPath = Path.Combine(rootPath, "destination");
		var sourceFilePath = Path.Combine(sourceFolderPath, "source.txt");
		var destinationFilePath = Path.Combine(destinationFolderPath, "source.txt");
		Directory.CreateDirectory(sourceFolderPath);
		Directory.CreateDirectory(destinationFolderPath);
		await File.WriteAllTextAsync(sourceFilePath, "Shell drag-and-drop content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var sourceItem = await ResolveAsync(source, sourceFilePath);
			var destinationItem = await ResolveAsync(source, destinationFolderPath);
			var preparedTarget = await source.DragDrop.PrepareDropTargetAsync(CreateReference(source, destinationItem), background: true);

			var result = await DropAsync(scheduler, preparedTarget, [sourceItem], WindowsShellDragDropModifiers.LeftButton, WindowsShellDropEffects.Copy);

			Assert.IsTrue(result.Created);
			Assert.IsTrue(result.Entered);
			Assert.AreEqual(WindowsShellDropEffects.Copy, result.EnterEffect);
			Assert.AreEqual(WindowsShellDropEffects.Copy, result.OverEffect);
			Assert.AreEqual(WindowsShellDropEffects.Copy, result.DropEffect);
			Assert.IsTrue(await WaitForFileContentAsync(destinationFilePath, "Shell drag-and-drop content", TimeSpan.FromSeconds(10)), "The Shell drop target did not create a readable copy of the file.");
		}
		finally
		{
			await DeleteTestRootAsync(rootPath);
		}
	}

	/// <summary>
	/// Test case: a Shell folder background drop target moves a native Shell data object.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NativeDropSessionMovesShellDataObject()
	{
		var rootPath = CreateTestRoot("BackgroundMove");
		var sourceFolderPath = Path.Combine(rootPath, "source");
		var destinationFolderPath = Path.Combine(rootPath, "destination");
		var sourceFilePath = Path.Combine(sourceFolderPath, "move.txt");
		var destinationFilePath = Path.Combine(destinationFolderPath, "move.txt");
		Directory.CreateDirectory(sourceFolderPath);
		Directory.CreateDirectory(destinationFolderPath);
		await File.WriteAllTextAsync(sourceFilePath, "Shell move content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var sourceItem = await ResolveAsync(source, sourceFilePath);
			var destinationItem = await ResolveAsync(source, destinationFolderPath);
			var preparedTarget = await source.DragDrop.PrepareDropTargetAsync(CreateReference(source, destinationItem), background: true);

			var allowedEffects = WindowsShellDropEffects.Copy | WindowsShellDropEffects.Move | WindowsShellDropEffects.Link;
			var result = await DropAsync(scheduler, preparedTarget, [sourceItem], WindowsShellDragDropModifiers.LeftButton | WindowsShellDragDropModifiers.Shift, allowedEffects);

			Assert.IsTrue(result.Created);
			Assert.IsTrue(result.Entered);
			Assert.AreEqual(WindowsShellDropEffects.Move, result.EnterEffect);
			Assert.AreEqual(WindowsShellDropEffects.Move, result.OverEffect);
			var moveCompleted = await WaitForConditionAsync(() => !File.Exists(sourceFilePath) && File.ReadAllText(destinationFilePath) == "Shell move content", TimeSpan.FromSeconds(10));
			var moveFailure = $"The Shell drop target did not move the file. Drop effect: {result.DropEffect}; "
				+ $"performed effect: {result.PerformedEffect}; paste succeeded: {result.PasteSucceededEffect}.";
			Assert.IsTrue(moveCompleted, moveFailure);
			Assert.IsTrue(
				result.DropEffect is WindowsShellDropEffects.Move || result.PerformedEffect is WindowsShellDropEffects.Move || result.PasteSucceededEffect is WindowsShellDropEffects.Move,
				$"The Shell target moved the file without reporting a move effect. Drop effect: {result.DropEffect}; performed effect: {result.PerformedEffect}; paste succeeded: {result.PasteSucceededEffect}.");
		}
		finally
		{
			await DeleteTestRootAsync(rootPath);
		}
	}

	/// <summary>
	/// Test case: a native Shell selection copies multiple files and a folder as one data object.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NativeDropSessionCopiesMultipleShellItems()
	{
		var rootPath = CreateTestRoot("MultipleItems");
		var sourceFolderPath = Path.Combine(rootPath, "source");
		var destinationFolderPath = Path.Combine(rootPath, "destination");
		var firstSourcePath = Path.Combine(sourceFolderPath, "first.txt");
		var secondSourcePath = Path.Combine(sourceFolderPath, "second.bin");
		var nestedSourcePath = Path.Combine(sourceFolderPath, "nested");
		var nestedFileSourcePath = Path.Combine(nestedSourcePath, "inside.txt");
		var firstDestinationPath = Path.Combine(destinationFolderPath, "first.txt");
		var secondDestinationPath = Path.Combine(destinationFolderPath, "second.bin");
		var nestedFileDestinationPath = Path.Combine(destinationFolderPath, "nested", "inside.txt");
		Directory.CreateDirectory(nestedSourcePath);
		Directory.CreateDirectory(destinationFolderPath);
		await File.WriteAllTextAsync(firstSourcePath, "first Shell item");
		await File.WriteAllBytesAsync(secondSourcePath, [0x10, 0x20, 0x30, 0x40]);
		await File.WriteAllTextAsync(nestedFileSourcePath, "nested Shell item");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var firstItem = await ResolveAsync(source, firstSourcePath);
			var secondItem = await ResolveAsync(source, secondSourcePath);
			var nestedItem = await ResolveAsync(source, nestedSourcePath);
			var destinationItem = await ResolveAsync(source, destinationFolderPath);
			var preparedTarget = await source.DragDrop.PrepareDropTargetAsync(CreateReference(source, destinationItem), background: true);

			var modifiers = WindowsShellDragDropModifiers.LeftButton | WindowsShellDragDropModifiers.Control;
			var result = await DropAsync(scheduler, preparedTarget, [firstItem, secondItem, nestedItem], modifiers, WindowsShellDropEffects.Copy);

			Assert.IsTrue(result.Created);
			Assert.IsTrue(result.Entered);
			Assert.AreEqual(WindowsShellDropEffects.Copy, result.DropEffect);
			var copiedAllItems = await WaitForConditionAsync(
				() => File.ReadAllText(firstDestinationPath) == "first Shell item"
					&& File.ReadAllBytes(secondDestinationPath).SequenceEqual(new byte[] { 0x10, 0x20, 0x30, 0x40 })
					&& File.ReadAllText(nestedFileDestinationPath) == "nested Shell item",
				TimeSpan.FromSeconds(10));
			Assert.IsTrue(copiedAllItems, "The Shell drop target did not create readable copies of the complete multi-item selection.");
		}
		finally
		{
			await DeleteTestRootAsync(rootPath);
		}
	}

	/// <summary>
	/// Test case: a Shell item-array data object copies selections whose items do not share a parent folder.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NativeDropSessionCopiesItemsFromDifferentParents()
	{
		var rootPath = CreateTestRoot("DifferentParents");
		var firstSourceFolderPath = Path.Combine(rootPath, "source-one");
		var secondSourceFolderPath = Path.Combine(rootPath, "source-two");
		var destinationFolderPath = Path.Combine(rootPath, "destination");
		var firstSourcePath = Path.Combine(firstSourceFolderPath, "first.txt");
		var secondSourcePath = Path.Combine(secondSourceFolderPath, "second.txt");
		var firstDestinationPath = Path.Combine(destinationFolderPath, "first.txt");
		var secondDestinationPath = Path.Combine(destinationFolderPath, "second.txt");
		Directory.CreateDirectory(firstSourceFolderPath);
		Directory.CreateDirectory(secondSourceFolderPath);
		Directory.CreateDirectory(destinationFolderPath);
		await File.WriteAllTextAsync(firstSourcePath, "first parent content");
		await File.WriteAllTextAsync(secondSourcePath, "second parent content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var firstItem = await ResolveAsync(source, firstSourcePath);
			var secondItem = await ResolveAsync(source, secondSourcePath);
			var destinationItem = await ResolveAsync(source, destinationFolderPath);
			var preparedTarget = await source.DragDrop.PrepareDropTargetAsync(CreateReference(source, destinationItem), background: true);
			var modifiers = WindowsShellDragDropModifiers.LeftButton | WindowsShellDragDropModifiers.Control;

			var result = await DropAsync(scheduler, preparedTarget, [firstItem, secondItem], modifiers, WindowsShellDropEffects.Copy);

			Assert.IsTrue(result.Created);
			Assert.IsTrue(result.Entered);
			Assert.AreEqual(WindowsShellDropEffects.Copy, result.DropEffect);
			var copiedAllItems = await WaitForConditionAsync(
				() => File.ReadAllText(firstDestinationPath) == "first parent content" && File.ReadAllText(secondDestinationPath) == "second parent content",
				TimeSpan.FromSeconds(10));
			Assert.IsTrue(copiedAllItems, "The Shell item-array data object did not create readable copies of items from different parents.");
		}
		finally
		{
			await DeleteTestRootAsync(rootPath);
		}
	}

	/// <summary>
	/// Test case: an individual Shell folder item accepts a native data object through its own drop target.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NativeItemDropTargetCopiesShellDataObject()
	{
		var rootPath = CreateTestRoot("ItemTarget");
		var sourceFolderPath = Path.Combine(rootPath, "source");
		var destinationFolderPath = Path.Combine(rootPath, "destination");
		var sourceFilePath = Path.Combine(sourceFolderPath, "item-target.txt");
		var destinationFilePath = Path.Combine(destinationFolderPath, "item-target.txt");
		Directory.CreateDirectory(sourceFolderPath);
		Directory.CreateDirectory(destinationFolderPath);
		await File.WriteAllTextAsync(sourceFilePath, "Shell item target content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var sourceItem = await ResolveAsync(source, sourceFilePath);
			var destinationItem = await ResolveAsync(source, destinationFolderPath);
			var preparedTarget = await source.DragDrop.PrepareDropTargetAsync(CreateReference(source, destinationItem), background: false);

			var result = await DropAsync(scheduler, preparedTarget, [sourceItem], WindowsShellDragDropModifiers.LeftButton | WindowsShellDragDropModifiers.Control, WindowsShellDropEffects.Copy);

			Assert.IsTrue(result.Created);
			Assert.IsTrue(result.Entered);
			Assert.AreEqual(WindowsShellDropEffects.Copy, result.DropEffect);
			Assert.IsTrue(await WaitForFileContentAsync(destinationFilePath, "Shell item target content", TimeSpan.FromSeconds(10)), "The Shell item drop target did not create a readable copy of the file.");
		}
		finally
		{
			await DeleteTestRootAsync(rootPath);
		}
	}

	/// <summary>
	/// Test case: one native drop session forwards modifier negotiation, leave, and reentry to the Shell target.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NativeDropSessionSupportsLeaveAndReentry()
	{
		var rootPath = CreateTestRoot("Lifecycle");
		var sourceFolderPath = Path.Combine(rootPath, "source");
		var destinationFolderPath = Path.Combine(rootPath, "destination");
		var sourceFilePath = Path.Combine(sourceFolderPath, "lifecycle.txt");
		var destinationFilePath = Path.Combine(destinationFolderPath, "lifecycle.txt");
		Directory.CreateDirectory(sourceFolderPath);
		Directory.CreateDirectory(destinationFolderPath);
		await File.WriteAllTextAsync(sourceFilePath, "Shell lifecycle content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var sourceItem = await ResolveAsync(source, sourceFilePath);
			var destinationItem = await ResolveAsync(source, destinationFolderPath);
			var preparedTarget = await source.DragDrop.PrepareDropTargetAsync(CreateReference(source, destinationItem), background: true);

			var result = await scheduler.InvokeAsync(() =>
			{
				var dataObject = WindowsShellDataObjectFactory.Create([sourceItem.Locator], default);
				if (!preparedTarget.TryCreateSession(dataObject, 0, out var session) || session is null)
				{
					return default(LifecycleResult);
				}

				using (session)
				{
					var controlModifiers = WindowsShellDragDropModifiers.LeftButton | WindowsShellDragDropModifiers.Control;
					var shiftModifiers = WindowsShellDragDropModifiers.LeftButton | WindowsShellDragDropModifiers.Shift;
					var allowedEffects = WindowsShellDropEffects.Copy | WindowsShellDropEffects.Move | WindowsShellDropEffects.Link;
					var controlEntered = session.TryDragEnter(controlModifiers, Point.Empty, allowedEffects, out var controlEnterEffect);
					var controlOverEffect = session.DragOver(controlModifiers, Point.Empty, allowedEffects);
					session.DragLeave();
					var shiftEntered = session.TryDragEnter(shiftModifiers, Point.Empty, allowedEffects, out var shiftEnterEffect);
					var shiftOverEffect = session.DragOver(shiftModifiers, Point.Empty, allowedEffects);
					session.DragLeave();
					var rightEntered = session.TryDragEnter(WindowsShellDragDropModifiers.RightButton, Point.Empty, allowedEffects, out var rightEnterEffect);
					var rightOverEffect = session.DragOver(WindowsShellDragDropModifiers.RightButton, Point.Empty, allowedEffects);
					session.DragLeave();
					var finalEntered = session.TryDragEnter(controlModifiers, Point.Empty, WindowsShellDropEffects.Copy, out var finalEnterEffect);
					var dropEffect = session.Drop(controlModifiers, Point.Empty, WindowsShellDropEffects.Copy);

					return new LifecycleResult(
						true,
						controlEntered, controlEnterEffect, controlOverEffect,
						shiftEntered, shiftEnterEffect, shiftOverEffect,
						rightEntered, rightEnterEffect, rightOverEffect,
						finalEntered, finalEnterEffect, dropEffect);
				}
			});

			Assert.IsTrue(result.Created);
			Assert.IsTrue(result.ControlEntered);
			Assert.AreEqual(WindowsShellDropEffects.Copy, result.ControlEnterEffect);
			Assert.AreEqual(WindowsShellDropEffects.Copy, result.ControlOverEffect);
			Assert.IsTrue(result.ShiftEntered);
			Assert.AreEqual(WindowsShellDropEffects.Move, result.ShiftEnterEffect);
			Assert.AreEqual(WindowsShellDropEffects.Move, result.ShiftOverEffect);
			Assert.IsTrue(result.RightEntered);
			Assert.AreNotEqual(WindowsShellDropEffects.None, result.RightEnterEffect);
			Assert.AreNotEqual(WindowsShellDropEffects.None, result.RightOverEffect);
			Assert.AreEqual(WindowsShellDropEffects.None, result.RightEnterEffect & ~(WindowsShellDropEffects.Copy | WindowsShellDropEffects.Move | WindowsShellDropEffects.Link));
			Assert.AreEqual(WindowsShellDropEffects.None, result.RightOverEffect & ~(WindowsShellDropEffects.Copy | WindowsShellDropEffects.Move | WindowsShellDropEffects.Link));
			Assert.IsTrue(result.FinalEntered);
			Assert.AreEqual(WindowsShellDropEffects.Copy, result.FinalEnterEffect);
			Assert.AreEqual(WindowsShellDropEffects.Copy, result.DropEffect);
			Assert.IsTrue(await WaitForFileContentAsync(destinationFilePath, "Shell lifecycle content", TimeSpan.FromSeconds(10)), "The Shell target did not create a readable copy after leave and reentry.");
		}
		finally
		{
			await DeleteTestRootAsync(rootPath);
		}
	}

	/// <summary>
	/// Test case: a native Shell data object round-trips transfer formats and the asynchronous capability lifecycle.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NativeDataObjectRoundTripsTransferContracts()
	{
		var rootPath = CreateTestRoot("DataObjectContracts");
		var filePath = Path.Combine(rootPath, "source.txt");
		Directory.CreateDirectory(rootPath);
		await File.WriteAllTextAsync(filePath, "Shell data-object content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var item = await ResolveAsync(source, filePath);

			var result = await scheduler.InvokeAsync(() =>
			{
				var dataObject = WindowsShellDataObjectFactory.Create([item.Locator], default);
				WindowsShellDataObjectFormat.SetDword(dataObject, WindowsShellDataObjectFormat.PreferredDropEffect, (uint)(WindowsShellDropEffects.Copy | WindowsShellDropEffects.Link));
				var foundPreferredEffect = WindowsShellDataObjectFormat.TryGetDword(dataObject, WindowsShellDataObjectFormat.PreferredDropEffect, out var preferredEffect);
				var foundInitialAsyncFlag = WindowsShellDataObjectFormat.TryGetDword(dataObject, WindowsShellDataObjectFormat.AsyncFlag, out _);
				if (dataObject is not IDataObjectAsyncCapability asyncCapability)
				{
					return new DataObjectContractResult(foundPreferredEffect, preferredEffect, foundInitialAsyncFlag, false, false, false, false, false, false, false, default, false);
				}

				var queriedDefaultAsyncMode = asyncCapability.GetAsyncMode(out var defaultIsAsync).Succeeded;
				var setAsyncModeSucceeded = asyncCapability.SetAsyncMode(true).Succeeded;
				var queriedEnabledAsyncMode = asyncCapability.GetAsyncMode(out var enabledIsAsync).Succeeded;
				var lifecycleSucceeded = false;
				var foundAsyncFlag = false;
				uint asyncFlag = default;
				if (setAsyncModeSucceeded && queriedEnabledAsyncMode && enabledIsAsync)
				{
					var startSucceeded = asyncCapability.StartOperation(null!).Succeeded;
					if (startSucceeded)
					{
						WindowsShellDataObjectFormat.SetDword(dataObject, WindowsShellDataObjectFormat.AsyncFlag, 1);
						foundAsyncFlag = WindowsShellDataObjectFormat.TryGetDword(dataObject, WindowsShellDataObjectFormat.AsyncFlag, out asyncFlag);
						var inOperation = asyncCapability.InOperation(out var isInOperation).Succeeded && isInOperation;
						var endSucceeded = asyncCapability.EndOperation(HRESULT.S_OK, null!, (uint)WindowsShellDropEffects.Copy).Succeeded;
						var endedOperation = asyncCapability.InOperation(out isInOperation).Succeeded && !isInOperation;
						lifecycleSucceeded = inOperation && endSucceeded && endedOperation;
					}
				}

				return new DataObjectContractResult(foundPreferredEffect, preferredEffect, foundInitialAsyncFlag, true, queriedDefaultAsyncMode, defaultIsAsync, setAsyncModeSucceeded,
					queriedEnabledAsyncMode, enabledIsAsync, foundAsyncFlag, asyncFlag, lifecycleSucceeded);
			});

			Assert.IsTrue(result.FoundPreferredEffect);
			Assert.AreEqual((uint)(WindowsShellDropEffects.Copy | WindowsShellDropEffects.Link), result.PreferredEffect);
			Assert.IsFalse(result.FoundInitialAsyncFlag);
			Assert.IsTrue(result.SupportsAsyncCapability);
			Assert.IsTrue(result.QueriedDefaultAsyncMode);
			Assert.IsFalse(result.DefaultIsAsync);
			Assert.IsTrue(result.SetAsyncModeSucceeded);
			Assert.IsTrue(result.QueriedEnabledAsyncMode);
			if (result.FoundAsyncFlag)
			{
				Assert.IsTrue(result.EnabledIsAsync);
				Assert.AreEqual(1U, result.AsyncFlag);
				Assert.IsTrue(result.AsyncLifecycleSucceeded);
			}
			else
			{
				Assert.IsFalse(result.AsyncLifecycleSucceeded);
			}
		}
		finally
		{
			await DeleteTestRootAsync(rootPath);
		}
	}

	/// <summary>
	/// Test case: the OLE clipboard copies multiple native Shell items and remains pasteable after being flushed.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NativeClipboardCopiesMultipleItemsAfterFlush()
	{
		RequireClipboardIntegrationTests();

		var rootPath = CreateTestRoot("ClipboardCopy");
		var sourceFolderPath = Path.Combine(rootPath, "source");
		var firstDestinationFolderPath = Path.Combine(rootPath, "destination-one");
		var secondDestinationFolderPath = Path.Combine(rootPath, "destination-two");
		var firstSourcePath = Path.Combine(sourceFolderPath, "first.txt");
		var secondSourcePath = Path.Combine(sourceFolderPath, "second.txt");
		Directory.CreateDirectory(sourceFolderPath);
		Directory.CreateDirectory(firstDestinationFolderPath);
		Directory.CreateDirectory(secondDestinationFolderPath);
		await File.WriteAllTextAsync(firstSourcePath, "first clipboard item");
		await File.WriteAllTextAsync(secondSourcePath, "second clipboard item");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			await using var clipboardRestore = await ClipboardRestoreScope.CaptureAsync(scheduler);
			var firstSourceItem = await ResolveAsync(source, firstSourcePath);
			var secondSourceItem = await ResolveAsync(source, secondSourcePath);
			var firstDestinationItem = await ResolveAsync(source, firstDestinationFolderPath);
			var secondDestinationItem = await ResolveAsync(source, secondDestinationFolderPath);
			var selection = new[] { CreateReference(source, firstSourceItem), CreateReference(source, secondSourceItem) };

			await source.Clipboard.SetItemsAsync(selection, move: false);
			await clipboardRestore.MarkPublishedClipboardAsync();
			var publishedFormats = await ReadClipboardFormatsAsync(scheduler);
			AssertPublishedClipboardContracts(publishedFormats, WindowsShellDropEffects.Copy | WindowsShellDropEffects.Link);
			Assert.IsTrue(await source.DragDrop.PasteAsync(CreateReference(source, firstDestinationItem), (nint)PInvoke.GetDesktopWindow()));
			var firstPasteCompleted = await WaitForConditionAsync(
				() => File.ReadAllText(Path.Combine(firstDestinationFolderPath, "first.txt")) == "first clipboard item"
					&& File.ReadAllText(Path.Combine(firstDestinationFolderPath, "second.txt")) == "second clipboard item",
				TimeSpan.FromSeconds(10));
			Assert.IsTrue(firstPasteCompleted, "The Shell paste command did not create readable copies of the complete selection.");

			await source.Clipboard.FlushAsync();
			var flushedFormats = await ReadClipboardFormatsAsync(scheduler);
			Assert.IsTrue(flushedFormats.FoundPreferredEffect);
			Assert.AreEqual((uint)(WindowsShellDropEffects.Copy | WindowsShellDropEffects.Link), flushedFormats.PreferredEffect);
			Assert.AreEqual(publishedFormats.FoundAsyncFlag, flushedFormats.FoundAsyncFlag);
			if (flushedFormats.FoundAsyncFlag)
			{
				Assert.AreEqual(1U, flushedFormats.AsyncFlag);
			}

			Assert.IsTrue(await source.DragDrop.PasteAsync(CreateReference(source, secondDestinationItem), (nint)PInvoke.GetDesktopWindow()));
			var secondPasteCompleted = await WaitForConditionAsync(
				() => File.ReadAllText(Path.Combine(secondDestinationFolderPath, "first.txt")) == "first clipboard item"
					&& File.ReadAllText(Path.Combine(secondDestinationFolderPath, "second.txt")) == "second clipboard item",
				TimeSpan.FromSeconds(10));
			Assert.IsTrue(secondPasteCompleted, "The flushed OLE clipboard did not create readable copies.");
		}
		finally
		{
			await DeleteTestRootAsync(rootPath);
		}
	}

	/// <summary>
	/// Test case: disposing a Windows storage source flushes its Shell clipboard data for another source to paste.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NativeClipboardRemainsPasteableAfterStorageSourceDisposal()
	{
		RequireClipboardIntegrationTests();

		var rootPath = CreateTestRoot("ClipboardSourceDisposal");
		var sourceFolderPath = Path.Combine(rootPath, "source");
		var destinationFolderPath = Path.Combine(rootPath, "destination");
		var sourceFilePath = Path.Combine(sourceFolderPath, "source.txt");
		var destinationFilePath = Path.Combine(destinationFolderPath, "source.txt");
		Directory.CreateDirectory(sourceFolderPath);
		Directory.CreateDirectory(destinationFolderPath);
		await File.WriteAllTextAsync(sourceFilePath, "clipboard shutdown content");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var consumingSource = new WindowsStorageSource(scheduler: scheduler);
			await using var clipboardRestore = await ClipboardRestoreScope.CaptureAsync(scheduler);
			await using (var publishingSource = new WindowsStorageSource(scheduler: scheduler))
			{
				var sourceItem = await ResolveAsync(publishingSource, sourceFilePath);
				await publishingSource.Clipboard.SetItemsAsync([CreateReference(publishingSource, sourceItem)], move: false);
				await clipboardRestore.MarkPublishedClipboardAsync();
				var publishedFormats = await ReadClipboardFormatsAsync(scheduler);
				AssertPublishedClipboardContracts(publishedFormats, WindowsShellDropEffects.Copy | WindowsShellDropEffects.Link);
			}

			var destinationItem = await ResolveAsync(consumingSource, destinationFolderPath);
			Assert.IsTrue(await consumingSource.DragDrop.PasteAsync(CreateReference(consumingSource, destinationItem), (nint)PInvoke.GetDesktopWindow()));
			var pasteCompleted = await WaitForFileContentAsync(destinationFilePath, "clipboard shutdown content", TimeSpan.FromSeconds(10));
			Assert.IsTrue(pasteCompleted, "Disposing the publishing source did not leave readable Shell clipboard data for a new source.");
		}
		finally
		{
			await DeleteTestRootAsync(rootPath);
		}
	}

	/// <summary>
	/// Test case: the OLE clipboard advertises and performs a native Shell move.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NativeClipboardMovesShellItems()
	{
		RequireClipboardIntegrationTests();

		var rootPath = CreateTestRoot("ClipboardMove");
		var sourceFolderPath = Path.Combine(rootPath, "source");
		var destinationFolderPath = Path.Combine(rootPath, "destination");
		var firstSourcePath = Path.Combine(sourceFolderPath, "first.txt");
		var secondSourcePath = Path.Combine(sourceFolderPath, "second.txt");
		var firstDestinationPath = Path.Combine(destinationFolderPath, "first.txt");
		var secondDestinationPath = Path.Combine(destinationFolderPath, "second.txt");
		Directory.CreateDirectory(sourceFolderPath);
		Directory.CreateDirectory(destinationFolderPath);
		await File.WriteAllTextAsync(firstSourcePath, "first moved clipboard item");
		await File.WriteAllTextAsync(secondSourcePath, "second moved clipboard item");

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			await using var clipboardRestore = await ClipboardRestoreScope.CaptureAsync(scheduler);
			var firstSourceItem = await ResolveAsync(source, firstSourcePath);
			var secondSourceItem = await ResolveAsync(source, secondSourcePath);
			var destinationItem = await ResolveAsync(source, destinationFolderPath);
			var selection = new[] { CreateReference(source, firstSourceItem), CreateReference(source, secondSourceItem) };

			await source.Clipboard.SetItemsAsync(selection, move: true);
			await clipboardRestore.MarkPublishedClipboardAsync();
			var publishedFormats = await ReadClipboardFormatsAsync(scheduler);
			AssertPublishedClipboardContracts(publishedFormats, WindowsShellDropEffects.Move);
			Assert.IsTrue(await source.DragDrop.PasteAsync(CreateReference(source, destinationItem), (nint)PInvoke.GetDesktopWindow()));
			var moveCompleted = await WaitForConditionAsync(
				() => !File.Exists(firstSourcePath) && !File.Exists(secondSourcePath)
					&& File.ReadAllText(firstDestinationPath) == "first moved clipboard item"
					&& File.ReadAllText(secondDestinationPath) == "second moved clipboard item",
				TimeSpan.FromSeconds(10));
			Assert.IsTrue(moveCompleted, "The Shell paste command did not create readable moved copies of the complete selection.");
		}
		finally
		{
			await DeleteTestRootAsync(rootPath);
		}
	}

	/// <summary>
	/// Test case: the inbox compressed-folder Shell extension accepts a file through its item drop target.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NativeCompressedFolderDropTargetAddsFile()
	{
		var rootPath = CreateTestRoot("CompressedFolder");
		var sourceFolderPath = Path.Combine(rootPath, "source");
		var sourceFilePath = Path.Combine(sourceFolderPath, "archive-entry.txt");
		var archivePath = Path.Combine(rootPath, "destination.zip");
		Directory.CreateDirectory(sourceFolderPath);
		await File.WriteAllTextAsync(sourceFilePath, "compressed Shell target content");
		using (ZipFile.Open(archivePath, ZipArchiveMode.Create))
		{
		}

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var sourceItem = await ResolveAsync(source, sourceFilePath);
			var archiveItem = await ResolveAsync(source, archivePath);
			var preparedTarget = await source.DragDrop.PrepareDropTargetAsync(CreateReference(source, archiveItem), background: false);

			var result = await DropAsync(scheduler, preparedTarget, [sourceItem], WindowsShellDragDropModifiers.LeftButton | WindowsShellDragDropModifiers.Control, WindowsShellDropEffects.Copy);
			if (!result.Created || !result.Entered)
			{
				Assert.Inconclusive("The inbox compressed-folder Shell extension did not expose an item drop target in this Windows environment.");
			}

			Assert.AreEqual(WindowsShellDropEffects.Copy, result.DropEffect);
			var fileAdded = await WaitForConditionAsync(
				() => TryReadZipEntry(archivePath, "archive-entry.txt", out var content) && content == "compressed Shell target content",
				TimeSpan.FromSeconds(10));
			Assert.IsTrue(fileAdded, "The compressed-folder Shell drop target did not add the file.");
		}
		finally
		{
			await DeleteTestRootAsync(rootPath);
		}
	}

	private static string CreateTestRoot(string scenario)
	{
		var path = Path.Combine(Path.GetTempPath(), $"Files.Core.ShellDragDropTests-{scenario}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);

		return path;
	}

	private static async Task<WindowsStorable> ResolveAsync(WindowsStorageSource source, string path)
	{
		return Assert.IsInstanceOfType<WindowsStorable>(await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, path)));
	}

	private static StorableReference CreateReference(WindowsStorageSource source, WindowsStorable item)
	{
		return new StorableReference(source.SourceId, item.Id, item.Address);
	}

	private static Task<DropResult> DropAsync(
		WindowsShellScheduler scheduler,
		WindowsShellDropTarget preparedTarget,
		IReadOnlyList<WindowsStorable> sourceItems,
		WindowsShellDragDropModifiers modifiers,
		WindowsShellDropEffects allowedEffects)
	{
		return scheduler.InvokeAsync(() =>
		{
			var dataObject = WindowsShellDataObjectFactory.Create(sourceItems.Select(item => item.Locator).ToArray(), default);
			if (!preparedTarget.TryCreateSession(dataObject, 0, out var session) || session is null)
			{
				return default(DropResult);
			}

			using (session)
			{
				var entered = session.TryDragEnter(modifiers, Point.Empty, allowedEffects, out var enterEffect);
				if (!entered)
				{
					return new DropResult(true, false, enterEffect, WindowsShellDropEffects.None, WindowsShellDropEffects.None, false, WindowsShellDropEffects.None, false, WindowsShellDropEffects.None);
				}

				var overEffect = session.DragOver(modifiers, Point.Empty, allowedEffects);
				var dropEffect = session.Drop(modifiers, Point.Empty, allowedEffects);
				var foundPerformedEffect = WindowsShellDataObjectFormat.TryGetDword(dataObject, PerformedDropEffectFormat, out var performedEffect);
				var foundPasteSucceeded = WindowsShellDataObjectFormat.TryGetDword(dataObject, PasteSucceededFormat, out var pasteSucceededEffect);

				return new DropResult(
					true, true, enterEffect, overEffect, dropEffect,
					foundPerformedEffect, (WindowsShellDropEffects)performedEffect,
					foundPasteSucceeded, (WindowsShellDropEffects)pasteSucceededEffect);
			}
		});
	}

	private static Task<ClipboardFormats> ReadClipboardFormatsAsync(WindowsShellScheduler scheduler)
	{
		return scheduler.InvokeAsync(() =>
		{
			var hr = PInvoke.OleGetClipboard(out var dataObject);
			if (hr.Failed || dataObject is null)
			{
				return default(ClipboardFormats);
			}

			var foundPreferredEffect = WindowsShellDataObjectFormat.TryGetDword(dataObject, WindowsShellDataObjectFormat.PreferredDropEffect, out var preferredEffect);
			var foundAsyncFlag = WindowsShellDataObjectFormat.TryGetDword(dataObject, WindowsShellDataObjectFormat.AsyncFlag, out var asyncFlag);
			if (dataObject is not IDataObjectAsyncCapability asyncCapability)
			{
				return new ClipboardFormats(foundPreferredEffect, preferredEffect, foundAsyncFlag, asyncFlag, false, false, false, false, false);
			}

			var queriedAsyncMode = asyncCapability.GetAsyncMode(out var isAsync).Succeeded;
			var queriedInOperation = asyncCapability.InOperation(out var isInOperation).Succeeded;

			return new ClipboardFormats(foundPreferredEffect, preferredEffect, foundAsyncFlag, asyncFlag, true, queriedAsyncMode, isAsync, queriedInOperation, isInOperation);
		});
	}

	private static void AssertPublishedClipboardContracts(ClipboardFormats formats, WindowsShellDropEffects preferredEffect)
	{
		Assert.IsTrue(formats.FoundPreferredEffect);
		Assert.AreEqual((uint)preferredEffect, formats.PreferredEffect);
		if (formats.FoundAsyncFlag)
		{
			Assert.AreEqual(1U, formats.AsyncFlag);
			Assert.IsTrue(formats.SupportsAsyncCapability);
			Assert.IsTrue(formats.QueriedAsyncMode);
			Assert.IsTrue(formats.IsAsync);
			Assert.IsTrue(formats.QueriedInOperation);
			Assert.IsTrue(formats.IsInOperation);
		}

		if (formats.QueriedInOperation)
		{
			Assert.AreEqual(formats.FoundAsyncFlag, formats.IsInOperation);
		}
	}

	private static void RequireClipboardIntegrationTests()
	{
		var isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
		var explicitlyEnabled = string.Equals(Environment.GetEnvironmentVariable("REFILES_RUN_CLIPBOARD_TESTS"), "1", StringComparison.Ordinal);
		if (!isCi && !explicitlyEnabled)
		{
			Assert.Inconclusive("Global OLE clipboard tests run only in CI or when REFILES_RUN_CLIPBOARD_TESTS=1 is explicitly set.");
		}
	}

	private static Task<bool> WaitForFileContentAsync(string path, string expectedContent, TimeSpan timeout)
	{
		return WaitForConditionAsync(() => File.ReadAllText(path) == expectedContent, timeout);
	}

	private static async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				if (condition())
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

		try
		{
			return condition();
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool TryReadZipEntry(string archivePath, string entryName, out string? content)
	{
		content = null;
		try
		{
			using var archive = ZipFile.OpenRead(archivePath);
			var entry = archive.GetEntry(entryName);
			if (entry is null)
			{
				return false;
			}

			using var reader = new StreamReader(entry.Open());
			content = reader.ReadToEnd();

			return true;
		}
		catch (InvalidDataException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
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

	private readonly record struct DropResult(
		bool Created, bool Entered,
		WindowsShellDropEffects EnterEffect, WindowsShellDropEffects OverEffect, WindowsShellDropEffects DropEffect,
		bool FoundPerformedEffect, WindowsShellDropEffects PerformedEffect,
		bool FoundPasteSucceeded, WindowsShellDropEffects PasteSucceededEffect);

	private readonly record struct LifecycleResult(
		bool Created,
		bool ControlEntered, WindowsShellDropEffects ControlEnterEffect, WindowsShellDropEffects ControlOverEffect,
		bool ShiftEntered, WindowsShellDropEffects ShiftEnterEffect, WindowsShellDropEffects ShiftOverEffect,
		bool RightEntered, WindowsShellDropEffects RightEnterEffect, WindowsShellDropEffects RightOverEffect,
		bool FinalEntered, WindowsShellDropEffects FinalEnterEffect, WindowsShellDropEffects DropEffect);

	private readonly record struct DataObjectContractResult(
		bool FoundPreferredEffect, uint PreferredEffect,
		bool FoundInitialAsyncFlag,
		bool SupportsAsyncCapability, bool QueriedDefaultAsyncMode, bool DefaultIsAsync,
		bool SetAsyncModeSucceeded, bool QueriedEnabledAsyncMode, bool EnabledIsAsync,
		bool FoundAsyncFlag, uint AsyncFlag, bool AsyncLifecycleSucceeded);

	private readonly record struct ClipboardFormats(
		bool FoundPreferredEffect, uint PreferredEffect,
		bool FoundAsyncFlag, uint AsyncFlag,
		bool SupportsAsyncCapability, bool QueriedAsyncMode, bool IsAsync,
		bool QueriedInOperation, bool IsInOperation);

	private readonly record struct ClipboardRestoreAttempt(bool SequenceMatched, bool ClipboardSet, HRESULT Result);

	private sealed class ClipboardRestoreScope : IAsyncDisposable
	{
		private readonly WindowsShellScheduler _scheduler;

		private IDataObject? _originalDataObject;
		private bool _isDisposed;
		private uint? _publishedSequenceNumber;

		private ClipboardRestoreScope(WindowsShellScheduler scheduler)
		{
			_scheduler = scheduler;
		}

		public static async Task<ClipboardRestoreScope> CaptureAsync(WindowsShellScheduler scheduler)
		{
			var scope = new ClipboardRestoreScope(scheduler);
			HRESULT lastResult = default;
			for (var attempt = 0; attempt < 20; attempt++)
			{
				lastResult = await scheduler.InvokeAsync(() => PInvoke.OleGetClipboard(out scope._originalDataObject));
				if (lastResult.Succeeded)
				{
					return scope;
				}

				await Task.Delay(50);
			}

			Assert.Inconclusive($"The OLE clipboard could not be captured safely. HRESULT: {lastResult}");

			return scope;
		}

		public Task MarkPublishedClipboardAsync()
		{
			return _scheduler.InvokeAsync(() =>
			{
				if (_publishedSequenceNumber.HasValue)
				{
					throw new InvalidOperationException("Clipboard ownership can only be marked once per restore scope.");
				}

				_publishedSequenceNumber = PInvoke.GetClipboardSequenceNumber();

				return true;
			});
		}

		public async ValueTask DisposeAsync()
		{
			if (_isDisposed)
			{
				return;
			}

			_isDisposed = true;
			if (_publishedSequenceNumber is not uint publishedSequenceNumber)
			{
				_originalDataObject = null;

				return;
			}

			HRESULT lastResult = default;
			for (var attempt = 0; attempt < 20; attempt++)
			{
				var restoreAttempt = await _scheduler.InvokeAsync(() =>
				{
					if (PInvoke.GetClipboardSequenceNumber() != publishedSequenceNumber)
					{
						return new ClipboardRestoreAttempt(false, false, default);
					}

					var hr = PInvoke.OleSetClipboard(_originalDataObject!);
					if (hr.Failed || _originalDataObject is null)
					{
						return new ClipboardRestoreAttempt(true, hr.Succeeded, hr);
					}

					hr = PInvoke.OleFlushClipboard();

					return new ClipboardRestoreAttempt(true, true, hr);
				});
				if (!restoreAttempt.SequenceMatched)
				{
					_originalDataObject = null;

					return;
				}

				lastResult = restoreAttempt.Result;
				if (restoreAttempt.ClipboardSet)
				{
					if (lastResult.Failed)
					{
						throw new InvalidOperationException($"The original OLE clipboard could not be flushed. HRESULT: {lastResult}");
					}

					_originalDataObject = null;

					return;
				}

				await Task.Delay(50);
			}

			throw new InvalidOperationException($"The original OLE clipboard could not be restored. HRESULT: {lastResult}");
		}
	}
}
