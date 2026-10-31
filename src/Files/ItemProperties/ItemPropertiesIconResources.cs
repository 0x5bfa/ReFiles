// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Files.ItemProperties;

internal sealed unsafe partial class ItemPropertiesIconResources
{
	private const int StockFolder = 3;
	private const int StockShield = 77;
	private const uint StockIconFlags = 0x00000100;

	[StructLayout(LayoutKind.Sequential)]
	private struct StockIconInfo
	{
		public uint Size;
		public nint Icon;
		public int SystemImageIndex;
		public int IconIndex;
		public fixed char Path[260];
	}

	[LibraryImport("shell32.dll", EntryPoint = "SHGetStockIconInfo")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial int SHGetStockIconInfo(int iconId, uint flags, ref StockIconInfo info);

	[LibraryImport("shell32.dll", EntryPoint = "SHDefExtractIconW", StringMarshalling = StringMarshalling.Utf16)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial int SHDefExtractIcon(string path, int index, uint flags, out nint largeIcon, out nint smallIcon, uint iconSize);

	[LibraryImport("user32.dll", EntryPoint = "DestroyIcon")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool DestroyIcon(nint icon);

	public Task<ReadOnlyMemory<byte>> GetElevationShieldIconAsync(int size = 16, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

		return Task.Run(() => ReadStockIcon(StockShield, size, cancellationToken), cancellationToken);
	}

	public Task<ReadOnlyMemory<byte>> GetFolderIconAsync(int size = 20, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

		return Task.Run(() => ReadStockIcon(StockFolder, size, cancellationToken), cancellationToken);
	}

	public Task<ReadOnlyMemory<byte>> GetResourceIconAsync(string path, int index, int size = 48, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

		return Task.Run(() => ReadResourceIcon(path, index, size, cancellationToken), cancellationToken);
	}

	private static ReadOnlyMemory<byte> ReadStockIcon(int iconId, int size, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		StockIconInfo info = default;
		info.Size = (uint)sizeof(StockIconInfo);
		if (SHGetStockIconInfo(iconId, StockIconFlags, ref info) < 0 || info.Icon is 0)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		try
		{
			return EncodeIcon(info.Icon, size, cancellationToken);
		}
		finally
		{
			DestroyIcon(info.Icon);
		}
	}

	private static ReadOnlyMemory<byte> ReadResourceIcon(string path, int index, int size, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var result = SHDefExtractIcon(path, index, 0, out var largeIcon, out var smallIcon, checked((uint)(size | size << 16)));
		try
		{
			return result >= 0 && largeIcon is not 0 ? EncodeIcon(largeIcon, size, cancellationToken) : ReadOnlyMemory<byte>.Empty;
		}
		finally
		{
			if (largeIcon is not 0)
			{
				DestroyIcon(largeIcon);
			}

			if (smallIcon is not 0)
			{
				DestroyIcon(smallIcon);
			}
		}
	}

	private static byte[] EncodeIcon(nint handle, int size, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var icon = Icon.FromHandle(handle);
		using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
		using (var graphics = Graphics.FromImage(bitmap))
		{
			graphics.Clear(Color.Transparent);
			graphics.DrawIcon(icon, new Rectangle(0, 0, size, size));
		}

		using var output = new MemoryStream();
		bitmap.Save(output, ImageFormat.Png);

		return output.ToArray();
	}
}
