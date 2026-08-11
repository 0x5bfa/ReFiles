// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Files.Core.ItemFeatures.Thumbnails;
using OwlCore.Storage;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for windows thumbnail behavior.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsThumbnailTests
{
	private static readonly byte[] PngSignature = [
		0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

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
	/// Test case: windows shell thumbnail is cached as independent png streams.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WindowsShellThumbnailIsCachedAsIndependentPngStreams()
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
			var featureRegistry = new ItemFeatureBuilder()
				.Add<IThumbnailSource>(new WindowsThumbnailSourceFactory(new WindowsShellThumbnailBackend()), origin: "Windows Shell")
				.SetCombiner<IThumbnailSource>(new ThumbnailSourceCombiner())
				.AddWrapper<IThumbnailSource>(new ThumbnailCacheWrapper(cache))
				.Build();

			using var model = new StorableModelFactory(featureRegistry).Create(source, coreModel);
			var thumbnailSource = model.Get<IThumbnailSource>();
			Assert.IsNotNull(thumbnailSource);

			var first = await thumbnailSource.GetThumbnailAsync(new ThumbnailRequest(96, ThumbnailMode.Icon));
			Assert.IsNotNull(first);
			Assert.AreEqual("image/png", first.ContentType);
			var firstContent = first.Content.ToArray();
			CollectionAssert.AreEqual(PngSignature, firstContent[..PngSignature.Length]);

			File.Delete(filePath);

			var second = await thumbnailSource.GetThumbnailAsync(new ThumbnailRequest(96, ThumbnailMode.Icon));
			Assert.IsNotNull(second);
			CollectionAssert.AreEqual(firstContent, second.Content.ToArray());
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}
