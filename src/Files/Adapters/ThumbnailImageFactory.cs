// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Media.Imaging;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;

namespace Files.Adapters;

internal static class ThumbnailImageFactory
{
	public static BitmapImage Create(ReadOnlyMemory<byte> encodedImage)
	{
		using var managedStream = CreateStream(encodedImage);
		using var randomAccessStream = managedStream.AsRandomAccessStream();
		var image = new BitmapImage();
		image.SetSource(randomAccessStream);

		return image;
	}

	public static async Task<BitmapImage> CreateAsync(ReadOnlyMemory<byte> encodedImage)
	{
		using var managedStream = CreateStream(encodedImage);
		using var randomAccessStream = managedStream.AsRandomAccessStream();
		var image = new BitmapImage();
		await image.SetSourceAsync(randomAccessStream);

		return image;
	}

	private static MemoryStream CreateStream(ReadOnlyMemory<byte> encodedImage)
	{
		if (MemoryMarshal.TryGetArray(encodedImage, out ArraySegment<byte> segment) && segment.Array is { } array)
		{
			return new MemoryStream(array, segment.Offset, segment.Count, writable: false, publiclyVisible: true);
		}

		return new MemoryStream(encodedImage.ToArray(), writable: false);
	}
}
