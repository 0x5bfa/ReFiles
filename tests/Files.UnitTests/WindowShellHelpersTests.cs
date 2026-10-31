// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;

namespace Files.UnitTests;

/// <summary>Tests Shell helper apartment requirements and item resolution.</summary>
[TestClass]
public sealed class WindowShellHelpersTests
{
	/// <summary>STA-only helpers reject MTA callers, including empty-input and identity fast paths.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task ResolutionAndComparisonRejectMtaCallers()
	{
		await Task.Run(() =>
		{
			Assert.AreEqual(ApartmentState.MTA, Thread.CurrentThread.GetApartmentState());
			var path = Path.Combine(Path.GetTempPath(), $"Files.ShellHelperTests-{Guid.NewGuid():N}");
			var locator = new WindowsItemLocator(ReadOnlyMemory<byte>.Empty, path);
			Assert.Throws<InvalidOperationException>(() => WindowShellHelpers.ResolveItem(path));
			Assert.Throws<InvalidOperationException>(() => WindowShellHelpers.TryResolveItem(path));
			Assert.Throws<InvalidOperationException>(() => WindowShellHelpers.TryResolveItem(locator));
			Assert.Throws<InvalidOperationException>(() => WindowShellHelpers.TryResolveItem(ReadOnlyMemory<byte>.Empty));
			Assert.Throws<InvalidOperationException>(() => WindowShellHelpers.AreSamePidl(ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty));
			ReadOnlyMemory<byte> pidl = new byte[] { 0, 0 };
			Assert.Throws<InvalidOperationException>(() => WindowShellHelpers.AreSamePidl(pidl, pidl));
			Assert.Throws<InvalidOperationException>(() => WindowShellHelpers.IsInFolder(ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, recursive: false));
		});
	}

	/// <summary>Shell items resolve by name and PIDL on STA, while missing items remain ordinary resolution failures.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task ResolutionAndComparisonWorkOnSta()
	{
		var directory = Path.Combine(Path.GetTempPath(), $"Files.ShellHelperTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, "item.txt");
		await File.WriteAllTextAsync(path, "content");
		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await scheduler.InvokeAsync(() =>
			{
				Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
				var itemIdReader = new WindowsItemIdReader();
				var item = WindowShellHelpers.ResolveItem(path);
				var descriptor = WindowShellHelpers.CreateDescriptor(item, itemIdReader);
				var parent = WindowShellHelpers.CreateDescriptor(WindowShellHelpers.ResolveItem(directory), itemIdReader);
				Assert.IsNotNull(WindowShellHelpers.TryResolveItem(path));
				Assert.IsNotNull(WindowShellHelpers.TryResolveItem(descriptor.Locator));
				Assert.IsNotNull(WindowShellHelpers.TryResolveItem(descriptor.Locator.AbsolutePidl));
				Assert.IsTrue(WindowShellHelpers.AreSamePidl(descriptor.Locator.AbsolutePidl, descriptor.Locator.AbsolutePidl));
				Assert.IsTrue(WindowShellHelpers.IsInFolder(descriptor.Locator.AbsolutePidl, parent.Locator.AbsolutePidl, recursive: false));
				var missingPath = Path.Combine(directory, "missing.txt");
				Assert.IsNull(WindowShellHelpers.TryResolveItem(missingPath));
				Assert.IsNull(WindowShellHelpers.TryResolveItem(new WindowsItemLocator(ReadOnlyMemory<byte>.Empty, missingPath)));
				Assert.IsNull(WindowShellHelpers.TryResolveItem(ReadOnlyMemory<byte>.Empty));
				Assert.IsFalse(WindowShellHelpers.AreSamePidl(ReadOnlyMemory<byte>.Empty, descriptor.Locator.AbsolutePidl));
				Assert.IsFalse(WindowShellHelpers.IsInFolder(ReadOnlyMemory<byte>.Empty, parent.Locator.AbsolutePidl, recursive: false));

				return true;
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}
}
