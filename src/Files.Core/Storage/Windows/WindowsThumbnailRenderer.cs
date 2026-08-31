// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Graphics.GdiPlus;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Converts Shell-owned bitmap and icon handles into independent PNG bytes.
/// </summary>
internal static unsafe class WindowsThumbnailRenderer
{
	private const uint BitmapInfoCompressionRgb = 0;
	private const byte SentinelBlue = 0x37;
	private const byte SentinelGreen = 0xA1;
	private const byte SentinelRed = 0xE9;
	private const int MaximumRenderSize = 4096;

	private static readonly Lazy<nuint> _gdiPlusToken = new(StartGdiPlus);
	private static readonly Lazy<Guid?> _pngEncoder = new(FindPngEncoder);

	public static byte[]? EncodeHBitmap(HBITMAP bitmap, CancellationToken cancellationToken, bool forceOpaque = false)
	{
		var rendered = RenderHBitmap(bitmap, cancellationToken, forceOpaque);

		return rendered is null ? null : EncodeBgra(rendered.Pixels, rendered.Width, rendered.Height, cancellationToken);
	}

	public static WindowsBitmapData? RenderHBitmap(HBITMAP bitmap, CancellationToken cancellationToken, bool forceOpaque = false)
	{
		if (bitmap.IsNull)
		{
			return null;
		}

		cancellationToken.ThrowIfCancellationRequested();

		BITMAP bitmapInfo = default;
		if (PInvoke.GetObject(new HGDIOBJ(bitmap.Value), sizeof(BITMAP), &bitmapInfo) is 0)
		{
			return null;
		}

		var width = Math.Abs(bitmapInfo.bmWidth);
		var height = Math.Abs(bitmapInfo.bmHeight);
		if (width is 0 || height is 0 || width > MaximumRenderSize || height > MaximumRenderSize)
		{
			return null;
		}

		var bgra = ReadBgra(bitmap, width, height);
		if (bgra.Length != checked(width * height * 4))
		{
			return null;
		}

		var hasAlpha = false;
		var hasRgb = false;
		for (var offset = 0; offset < bgra.Length; offset += 4)
		{
			hasAlpha |= bgra[offset + 3] is not 0;
			hasRgb |= (bgra[offset] | bgra[offset + 1] | bgra[offset + 2]) is not 0;
		}

		if (forceOpaque || (!hasAlpha && hasRgb))
		{
			SetOpaqueAlpha(bgra);
		}

		return new WindowsBitmapData(bgra, width, height);
	}

	public static byte[]? EncodeHBitmap(SafeHandle bitmap, CancellationToken cancellationToken, bool forceOpaque = false)
	{
		ArgumentNullException.ThrowIfNull(bitmap);

		if (bitmap.IsInvalid)
		{
			return null;
		}

		var addedReference = false;
		try
		{
			bitmap.DangerousAddRef(ref addedReference);

			return EncodeHBitmap((HBITMAP)bitmap.DangerousGetHandle(), cancellationToken, forceOpaque);
		}
		finally
		{
			if (addedReference)
			{
				bitmap.DangerousRelease();
			}
		}
	}

	public static WindowsBitmapData? RenderHBitmap(SafeHandle bitmap, CancellationToken cancellationToken, bool forceOpaque = false)
	{
		ArgumentNullException.ThrowIfNull(bitmap);

		if (bitmap.IsInvalid)
		{
			return null;
		}

		var addedReference = false;
		try
		{
			bitmap.DangerousAddRef(ref addedReference);

			return RenderHBitmap((HBITMAP)bitmap.DangerousGetHandle(), cancellationToken, forceOpaque);
		}
		finally
		{
			if (addedReference)
			{
				bitmap.DangerousRelease();
			}
		}
	}

	public static byte[]? EncodeHIcon(SafeHandle icon, int size, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(icon);

		if (icon.IsInvalid || size is <= 0 or > MaximumRenderSize)
		{
			return null;
		}

		var addedReference = false;
		try
		{
			icon.DangerousAddRef(ref addedReference);
			var rawIcon = (HICON)icon.DangerousGetHandle();
			var encodedIcon = EncodeHIconWithWic(rawIcon, size, cancellationToken);
			if (encodedIcon is not null)
			{
				return encodedIcon;
			}

			var bgra = RenderHIcon(rawIcon, size, cancellationToken);

			return bgra is null
				? null
				: EncodeBgra(bgra, size, size, cancellationToken);
		}
		finally
		{
			if (addedReference)
			{
				icon.DangerousRelease();
			}
		}
	}

	public static bool TryCompositeOverlay(ReadOnlyMemory<byte> png, SafeHandle overlayIcon, out byte[] compositedPng, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(overlayIcon);
		cancellationToken.ThrowIfCancellationRequested();

		compositedPng = png.ToArray();
		if (overlayIcon.IsInvalid || !TryDecodePng(png, out var baseBgra, out var width, out var height, cancellationToken) || width <= 0 || height <= 0 || width != height)
		{
			return false;
		}

		var overlayBgra = RenderHIcon(overlayIcon, width, cancellationToken);
		if (overlayBgra is null || overlayBgra.Length != baseBgra.Length)
		{
			return false;
		}

		var result = CompositeBgra(baseBgra, overlayBgra);
		var encoded = EncodeBgra(result, width, height, cancellationToken);
		if (encoded is null)
		{
			return false;
		}

		compositedPng = encoded;

		return true;
	}

	public static bool TryCompositeOverlay(WindowsBitmapData bitmap, SafeHandle overlayIcon, out WindowsBitmapData compositedBitmap, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(bitmap);
		ArgumentNullException.ThrowIfNull(overlayIcon);

		cancellationToken.ThrowIfCancellationRequested();

		compositedBitmap = bitmap;
		if (overlayIcon.IsInvalid || bitmap.Width <= 0 || bitmap.Height <= 0 || bitmap.Width != bitmap.Height || bitmap.Pixels.Length != checked(bitmap.Width * bitmap.Height * 4))
		{
			return false;
		}

		var overlayBgra = RenderHIcon(overlayIcon, bitmap.Width, cancellationToken);
		if (overlayBgra is null || overlayBgra.Length != bitmap.Pixels.Length)
		{
			return false;
		}

		compositedBitmap = new WindowsBitmapData(CompositeBgra(bitmap.Pixels, overlayBgra), bitmap.Width, bitmap.Height);

		return true;
	}

	internal static bool TryCropEncodedImage(ReadOnlyMemory<byte> image, int x, int y, int width, int height, out byte[] croppedImage, CancellationToken cancellationToken)
	{
		croppedImage = [];
		if (!TryDecodePng(image, out var source, out var sourceWidth, out var sourceHeight, cancellationToken)
			|| x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > sourceWidth || y + height > sourceHeight)
		{
			return false;
		}

		var cropped = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
		for (var row = 0; row < height; row++)
		{
			var sourceOffset = checked(((y + row) * sourceWidth + x) * 4);
			var destinationOffset = checked(row * width * 4);
			Buffer.BlockCopy(source, sourceOffset, cropped, destinationOffset, checked(width * 4));
		}

		var encoded = EncodeBgra(cropped, width, height, cancellationToken);
		if (encoded is null)
		{
			return false;
		}

		croppedImage = encoded;

		return true;
	}

	private static byte[]? EncodeHIconWithWic(HICON icon, int expectedSize, CancellationToken cancellationToken)
	{
		var createFactoryResult = PInvoke.CoCreateInstance(PInvoke.CLSID_WICImagingFactory2, null!, CLSCTX.CLSCTX_INPROC_SERVER, out IWICImagingFactory factory);
		if (createFactoryResult.Failed || factory.CreateBitmapFromHICON(icon, out var bitmap).Failed || bitmap.GetSize(out var width, out var height).Failed
			|| width != expectedSize || height != expectedSize)
		{
			return null;
		}

		if (factory.CreateFormatConverter(out var converter).Failed)
		{
			return null;
		}

		var pixelFormat = PInvoke.GUID_WICPixelFormat32bppBGRA;
		if (converter.Initialize(bitmap, &pixelFormat, WICBitmapDitherType.WICBitmapDitherTypeNone, null!, 0, WICBitmapPaletteType.WICBitmapPaletteTypeCustom).Failed)
		{
			return null;
		}

		var stride = checked(width * 4);
		var bufferSize = checked(stride * height);
		var bgra = GC.AllocateUninitializedArray<byte>(checked((int)bufferSize));
		if (converter.CopyPixels(null, stride, bufferSize, bgra).Failed)
		{
			return null;
		}

		return EncodeBgra(bgra, checked((int)width), checked((int)height), cancellationToken);
	}

	private static byte[]? EncodeBgra(byte[] bgra, int width, int height, CancellationToken cancellationToken)
	{
		if (width <= 0 || height <= 0 || width > MaximumRenderSize || height > MaximumRenderSize || bgra.Length != checked(width * height * 4))
		{
			return null;
		}

		_ = _gdiPlusToken.Value;
		var encoder = _pngEncoder.Value;
		if (encoder is not { } encoderClsid)
		{
			return null;
		}

		cancellationToken.ThrowIfCancellationRequested();

		GpBitmap* bitmap = null;
		fixed (byte* scan0 = bgra)
		{
			var createResult = PInvoke.GdipCreateBitmapFromScan0(width, height, checked(width * 4), PInvoke.PixelFormat32bppARGB, scan0, &bitmap);
			if (createResult is not Status.Ok || bitmap is null)
			{
				return null;
			}

			try
			{
				return EncodeImage((GpImage*)bitmap, encoderClsid, cancellationToken);
			}
			finally
			{
				PInvoke.GdipDisposeImage((GpImage*)bitmap);
			}
		}
	}

	private static byte[]? EncodeImage(GpImage* image, Guid encoderClsid, CancellationToken cancellationToken)
	{
		var streamResult = PInvoke.CreateStreamOnHGlobal(HGLOBAL.Null, true, out IStream stream);
		if (streamResult.Failed)
		{
			return null;
		}

		cancellationToken.ThrowIfCancellationRequested();

		if (PInvoke.GdipSaveImageToStream(image, stream, &encoderClsid, (EncoderParameters*)null) is not Status.Ok)
		{
			return null;
		}

		if (stream.Stat(out var stat, STATFLAG.STATFLAG_NONAME).Failed || stat.cbSize > int.MaxValue)
		{
			return null;
		}

		var content = GC.AllocateUninitializedArray<byte>((int)stat.cbSize);
		if (stream.Seek(0, SeekOrigin.Begin).Failed)
		{
			return null;
		}

		if (content.Length is not 0 && stream.Read(content).Failed)
		{
			return null;
		}

		cancellationToken.ThrowIfCancellationRequested();

		return content;
	}

	private static bool TryDecodePng(ReadOnlyMemory<byte> png, out byte[] bgra, out int width, out int height, CancellationToken cancellationToken)
	{
		bgra = [];
		width = 0;
		height = 0;
		cancellationToken.ThrowIfCancellationRequested();

		if (png.IsEmpty)
		{
			return false;
		}

		var streamResult = PInvoke.CreateStreamOnHGlobal(HGLOBAL.Null, true, out IStream stream);
		if (streamResult.Failed || stream.Write(png.Span).Failed || stream.Seek(0, SeekOrigin.Begin).Failed)
		{
			return false;
		}

		GpImage* image = null;
		var loadResult = PInvoke.GdipLoadImageFromStream(stream, &image);
		if (loadResult is not Status.Ok || image is null)
		{
			return false;
		}

		HBITMAP bitmap = default;
		try
		{
			var bitmapResult = PInvoke.GdipCreateHBITMAPFromBitmap((GpBitmap*)image, &bitmap, 0);
			if (bitmapResult is not Status.Ok || bitmap.IsNull)
			{
				return false;
			}

			BITMAP bitmapInfo = default;
			if (PInvoke.GetObject(new HGDIOBJ(bitmap.Value), sizeof(BITMAP), &bitmapInfo) is 0)
			{
				return false;
			}

			width = Math.Abs(bitmapInfo.bmWidth);
			height = Math.Abs(bitmapInfo.bmHeight);
			if (width is 0 || height is 0 || width > MaximumRenderSize || height > MaximumRenderSize)
			{
				return false;
			}

			bgra = ReadBgra(bitmap, width, height);

			return bgra.Length == checked(width * height * 4);
		}
		finally
		{
			if (!bitmap.IsNull)
			{
				PInvoke.DeleteObject(bitmap);
			}

			PInvoke.GdipDisposeImage(image);
		}
	}

	private static byte[] ReadBgra(HBITMAP bitmap, int width, int height)
	{
		var screenDc = PInvoke.GetDC(default);
		if (screenDc.IsNull)
		{
			return [];
		}

		try
		{
			var bitmapInfo = CreateBitmapInfo(width, height);
			var result = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
			fixed (byte* destination = result)
			{
				var scanLines = PInvoke.GetDIBits(screenDc, bitmap, 0, (uint)height, destination, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS);

				return scanLines == height ? result : [];
			}
		}
		finally
		{
			PInvoke.ReleaseDC(default, screenDc);
		}
	}

	private static byte[]? RenderHIcon(HICON icon, int size, CancellationToken cancellationToken)
	{
		if (icon.IsNull || size <= 0 || size > MaximumRenderSize)
		{
			return null;
		}

		cancellationToken.ThrowIfCancellationRequested();

		var screenDc = PInvoke.GetDC(default);
		var memoryDc = default(HDC);
		DeleteObjectSafeHandle? bitmap = null;
		var oldBitmap = default(HGDIOBJ);
		try
		{
			if (screenDc.IsNull || (memoryDc = PInvoke.CreateCompatibleDC(default)).IsNull)
			{
				return null;
			}

			var bitmapInfo = CreateBitmapInfo(size, size);
			bitmap = PInvoke.CreateDIBSection(screenDc, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS, out var bits, null, 0);
			if (bitmap.IsInvalid || bits is null)
			{
				return null;
			}

			var byteCount = checked(size * size * 4);
			var destination = new Span<byte>(bits, byteCount);
			for (var offset = 0; offset < destination.Length; offset += 4)
			{
				destination[offset] = SentinelBlue;
				destination[offset + 1] = SentinelGreen;
				destination[offset + 2] = SentinelRed;
				destination[offset + 3] = 0;
			}

			oldBitmap = PInvoke.SelectObject(memoryDc, new HGDIOBJ(bitmap.DangerousGetHandle()));
			if (oldBitmap.IsNull || PInvoke.DrawIconEx(memoryDc, 0, 0, icon, size, size, 0, default, DI_FLAGS.DI_NORMAL).Value == 0)
			{
				return null;
			}

			PInvoke.GdiFlush();
			var bgra = GC.AllocateUninitializedArray<byte>(byteCount);
			Marshal.Copy(new IntPtr(bits), bgra, 0, bgra.Length);
			SetRenderedAlpha(bgra);

			return bgra;
		}
		finally
		{
			if (!oldBitmap.IsNull)
			{
				PInvoke.SelectObject(memoryDc, oldBitmap);
			}

			bitmap?.Dispose();
			if (!memoryDc.IsNull)
			{
				PInvoke.DeleteDC(memoryDc);
			}

			if (!screenDc.IsNull)
			{
				PInvoke.ReleaseDC(default, screenDc);
			}
		}
	}

	private static byte[]? RenderHIcon(SafeHandle icon, int size, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(icon);

		if (icon.IsInvalid)
		{
			return null;
		}

		var addedReference = false;
		try
		{
			icon.DangerousAddRef(ref addedReference);

			return RenderHIcon((HICON)icon.DangerousGetHandle(), size, cancellationToken);
		}
		finally
		{
			if (addedReference)
			{
				icon.DangerousRelease();
			}
		}
	}

	private static byte[] CompositeBgra(byte[] baseBgra, byte[] overlayBgra)
	{
		var result = new byte[baseBgra.Length];
		for (var offset = 0; offset < baseBgra.Length; offset += 4)
		{
			var sourceAlpha = overlayBgra[offset + 3];
			if (sourceAlpha is 0)
			{
				baseBgra.AsSpan(offset, 4).CopyTo(result.AsSpan(offset, 4));
				continue;
			}

			var destinationAlpha = baseBgra[offset + 3];
			var inverseSourceAlpha = 255 - sourceAlpha;
			var resultAlpha = sourceAlpha
				+ (destinationAlpha * inverseSourceAlpha + 127) / 255;
			if (resultAlpha is 0)
			{
				continue;
			}

			result[offset] = (byte)((overlayBgra[offset] * sourceAlpha + baseBgra[offset] * destinationAlpha * inverseSourceAlpha / 255) / resultAlpha);
			result[offset + 1] = (byte)((overlayBgra[offset + 1] * sourceAlpha + baseBgra[offset + 1] * destinationAlpha * inverseSourceAlpha / 255) / resultAlpha);
			result[offset + 2] = (byte)((overlayBgra[offset + 2] * sourceAlpha + baseBgra[offset + 2] * destinationAlpha * inverseSourceAlpha / 255) / resultAlpha);
			result[offset + 3] = (byte)resultAlpha;
		}

		return result;
	}

	private static BITMAPINFO CreateBitmapInfo(int width, int height) => new()
	{
		bmiHeader = new BITMAPINFOHEADER
		{
			biSize = (uint)sizeof(BITMAPINFOHEADER),
			biWidth = width,
			biHeight = -height,
			biPlanes = 1,
			biBitCount = 32,
			biCompression = BitmapInfoCompressionRgb,
		},
	};

	private static void SetRenderedAlpha(byte[] bgra)
	{
		var hasSourceAlpha = false;
		for (var offset = 3; offset < bgra.Length; offset += 4)
		{
			if (bgra[offset] is not 0)
			{
				hasSourceAlpha = true;

				break;
			}
		}

		for (var offset = 0; offset < bgra.Length; offset += 4)
		{
			var wasDrawn = bgra[offset] != SentinelBlue || bgra[offset + 1] != SentinelGreen || bgra[offset + 2] != SentinelRed;
			if (wasDrawn && !hasSourceAlpha)
			{
				bgra[offset + 3] = 255;
			}
			else if (!wasDrawn)
			{
				bgra[offset] = 0;
				bgra[offset + 1] = 0;
				bgra[offset + 2] = 0;
				bgra[offset + 3] = 0;
			}
		}
	}

	private static void SetOpaqueAlpha(byte[] bgra)
	{
		for (var offset = 3; offset < bgra.Length; offset += 4)
		{
			bgra[offset] = 255;
		}
	}

	private static Guid? FindPngEncoder()
	{
		if (PInvoke.GdipGetImageEncodersSize(out var count, out var size) is not Status.Ok || count is 0 || size is 0)
		{
			return null;
		}

		var codecs = (ImageCodecInfo*)NativeMemory.Alloc(size);
		try
		{
			if (PInvoke.GdipGetImageEncoders(count, size, codecs) is not Status.Ok)
			{
				return null;
			}

			for (var index = 0U; index < count; index++)
			{
				if (codecs[index].FormatID == PInvoke.ImageFormatPNG)
				{
					return codecs[index].Clsid;
				}
			}

			return null;
		}
		finally
		{
			NativeMemory.Free(codecs);
		}
	}

	private static nuint StartGdiPlus()
	{
		var input = new GdiplusStartupInput
		{
			GdiplusVersion = 1,
		};
		var output = default(GdiplusStartupOutput);
		nuint token = 0;
		var result = PInvoke.GdiplusStartup(ref token, input, ref output);
		if (result is not Status.Ok)
		{
			throw new InvalidOperationException($"Failed to initialize GDI+. Status: {result}.");
		}

		return token;
	}
}

internal sealed record WindowsBitmapData(byte[] Pixels, int Width, int Height);
