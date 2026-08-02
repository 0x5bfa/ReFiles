// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;

namespace Files.Adapters;

internal static class ThumbnailImageFactory
{
	public static async Task<BitmapImage> CreateAsync(ReadOnlyMemory<byte> encodedImage)
	{
		using var managedStream = new MemoryStream(encodedImage.ToArray(), writable: false);
		using var randomAccessStream = managedStream.AsRandomAccessStream();
		var image = new BitmapImage();
		await image.SetSourceAsync(randomAccessStream);
		return image;
	}
}
