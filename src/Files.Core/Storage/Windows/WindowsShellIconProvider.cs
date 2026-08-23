// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Loads the system icons used by Windows Shell property pages.
/// </summary>
public static unsafe class WindowsShellIconProvider
{
	/// <summary>
	/// Loads the stock UAC elevation shield.
	/// </summary>
	/// <param name="size">The square PNG size in pixels.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The encoded PNG, or an empty value when the icon cannot be loaded.</returns>
	public static ReadOnlyMemory<byte> GetElevationShieldIcon(int size = 16, CancellationToken cancellationToken = default)
	{
		return GetStockIcon(SHSTOCKICONID.SIID_SHIELD, size, cancellationToken);
	}

	/// <summary>
	/// Loads the stock closed-folder icon.
	/// </summary>
	/// <param name="size">The square PNG size in pixels.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The encoded PNG, or an empty value when the icon cannot be loaded.</returns>
	public static ReadOnlyMemory<byte> GetFolderIcon(int size = 20, CancellationToken cancellationToken = default)
	{
		return GetStockIcon(SHSTOCKICONID.SIID_FOLDER, size, cancellationToken);
	}

	/// <summary>
	/// Loads the Shell icon for a file-system object, including registered overlays.
	/// </summary>
	/// <param name="path">The file-system path.</param>
	/// <param name="size">The square PNG size in pixels.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The encoded PNG, or an empty value when the icon cannot be loaded.</returns>
	public static ReadOnlyMemory<byte> GetFileSystemIcon(string path, int size = 32, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		var info = new SHFILEINFOW();
		var flags = SHGFI_FLAGS.SHGFI_ICON | SHGFI_FLAGS.SHGFI_ADDOVERLAYS | SHGFI_FLAGS.SHGFI_LARGEICON;
		if (PInvoke.SHGetFileInfo(path, 0, ref info, flags) is 0 || info.hIcon.IsNull)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		using var icon = new DestroyIconSafeHandle((nint)info.hIcon.Value);

		return WindowsThumbnailRenderer.EncodeHIcon(icon, size, cancellationToken) ?? [];
	}

	/// <summary>
	/// Loads an icon from a Shell icon resource.
	/// </summary>
	/// <param name="path">The icon resource path.</param>
	/// <param name="index">The icon resource index.</param>
	/// <param name="size">The square PNG size in pixels.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The encoded PNG, or an empty value when the icon cannot be loaded.</returns>
	public static ReadOnlyMemory<byte> GetResourceIcon(string path, int index, int size = 48, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		var result = PInvoke.SHDefExtractIcon(path, index, 0, out var largeIcon, out var smallIcon, checked((uint)(size | size << 16)));
		using (largeIcon)
		using (smallIcon)
		{
			if (result.Failed || largeIcon.IsInvalid)
			{
				return ReadOnlyMemory<byte>.Empty;
			}

			return WindowsThumbnailRenderer.EncodeHIcon(largeIcon, size, cancellationToken) ?? [];
		}
	}

	internal static ReadOnlyMemory<byte> GetStockIcon(SHSTOCKICONID stockIcon, int size, CancellationToken cancellationToken)
	{
		var info = new SHSTOCKICONINFO { cbSize = (uint)sizeof(SHSTOCKICONINFO) };
		if (PInvoke.SHGetStockIconInfo(stockIcon, SHGSI_FLAGS.SHGSI_ICON | SHGSI_FLAGS.SHGSI_LARGEICON, ref info).Failed || info.hIcon.IsNull)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		using var icon = new DestroyIconSafeHandle((nint)info.hIcon.Value);

		return WindowsThumbnailRenderer.EncodeHIcon(icon, size, cancellationToken) ?? [];
	}
}
