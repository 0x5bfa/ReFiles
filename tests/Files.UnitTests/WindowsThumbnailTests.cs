// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Files.Core.Capabilities.Thumbnails;
using OwlCore.Storage;
using Windows.Win32.UI.Shell;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for windows thumbnail behavior.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsThumbnailTests
{
	private static readonly byte[] OnePixelPng = [
		0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
		0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
		0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
		0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
		0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
		0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0xF0,
		0x1F, 0x00, 0x05, 0x00, 0x01, 0xFF, 0x89, 0x99,
		0x3D, 0x1D, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45,
		0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

	/// <summary>
	/// Test case: windows shell icons are cached as encoded PNG images.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WindowsShellIconIsCachedAsEncodedPng()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ThumbnailTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var filePath = Path.Combine(directoryPath, "thumbnail.png");
		File.WriteAllBytes(filePath, OnePixelPng);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var coreModel = await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, filePath));
			var windowsModel = Assert.IsInstanceOfType<WindowsStorable>(coreModel);
			var resolver = new WindowsShellItemResolver(scheduler);
			Assert.IsTrue(await resolver.InvokeConcurrentAsync(windowsModel.Locator, static shellItem => shellItem is IShellItem2));

			var cache = new MemoryThumbnailCache();
			var capabilityRegistry = new CapabilityBuilder()
				.Add<IThumbnailSource>(new WindowsThumbnailSourceFactory(new WindowsShellThumbnailBackend()), origin: "Windows Shell")
				.SetCombiner<IThumbnailSource>(new ThumbnailSourceCombiner())
				.AddWrapper<IThumbnailSource>(new ThumbnailCacheWrapper(cache))
				.Build();

			using var model = new StorableModelFactory(capabilityRegistry).Create(source, coreModel);
			var thumbnailSource = model.Get<IThumbnailSource>();
			Assert.IsNotNull(thumbnailSource);

			var first = await thumbnailSource.GetThumbnailAsync(new ThumbnailRequest(96, ThumbnailMode.Icon));
			Assert.IsNotNull(first);
			Assert.AreEqual(ThumbnailContentFormat.EncodedImage, first.Format);
			Assert.AreEqual("image/png", first.ContentType);
			CollectionAssert.AreEqual(OnePixelPng.AsSpan(0, 8).ToArray(), first.Content.Span[..8].ToArray());
			var firstContent = first.Content.ToArray();

			File.Delete(filePath);

			var second = await thumbnailSource.GetThumbnailAsync(new ThumbnailRequest(96, ThumbnailMode.Icon));
			Assert.IsNotNull(second);
			Assert.AreEqual(ThumbnailContentFormat.EncodedImage, second.Format);
			CollectionAssert.AreEqual(firstContent, second.Content.ToArray());
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Test case: windows shell content thumbnails are cached as raw BGRA pixels.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WindowsShellContentThumbnailIsCachedAsRawBgraPixels()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ThumbnailTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var filePath = Path.Combine(directoryPath, "thumbnail.png");
		File.WriteAllBytes(filePath, OnePixelPng);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var coreModel = await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, filePath));

			var cache = new MemoryThumbnailCache();
			var capabilityRegistry = new CapabilityBuilder()
				.Add<IThumbnailSource>(new WindowsThumbnailSourceFactory(new WindowsShellThumbnailBackend()), origin: "Windows Shell")
				.SetCombiner<IThumbnailSource>(new ThumbnailSourceCombiner())
				.AddWrapper<IThumbnailSource>(new ThumbnailCacheWrapper(cache))
				.Build();

			using var model = new StorableModelFactory(capabilityRegistry).Create(source, coreModel);
			var thumbnailSource = model.Get<IThumbnailSource>();
			Assert.IsNotNull(thumbnailSource);

			var first = await thumbnailSource.GetThumbnailAsync(new ThumbnailRequest(96, ThumbnailMode.Content));
			Assert.IsNotNull(first);
			Assert.AreEqual(ThumbnailContentFormat.Bgra8, first.Format);
			Assert.AreEqual(checked(first.PixelWidth * first.PixelHeight * 4), first.Content.Length);
			var firstContent = first.Content.ToArray();

			File.Delete(filePath);

			var second = await thumbnailSource.GetThumbnailAsync(new ThumbnailRequest(96, ThumbnailMode.Content));
			Assert.IsNotNull(second);
			Assert.AreEqual(first.PixelWidth, second.PixelWidth);
			Assert.AreEqual(first.PixelHeight, second.PixelHeight);
			CollectionAssert.AreEqual(firstContent, second.Content.ToArray());

			var resized = await thumbnailSource.GetThumbnailAsync(new ThumbnailRequest(48, ThumbnailMode.Content));
			Assert.IsNotNull(resized);
			Assert.AreEqual(ThumbnailContentFormat.Bgra8, resized.Format);
			Assert.AreEqual(checked(resized.PixelWidth * resized.PixelHeight * 4), resized.Content.Length);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}
