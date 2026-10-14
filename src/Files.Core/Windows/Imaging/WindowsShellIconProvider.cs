// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

/// <summary>
/// Loads the system icons used by Windows Shell property pages.
/// </summary>
public static unsafe class WindowsShellIconProvider
{
	private const int ErrorInsufficientBuffer = 122;

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

	internal static ReadOnlyMemory<byte> GetAppExtensionIcon(string path, int index, int size, CancellationToken cancellationToken,
		string? packageFullName = null, string? fallbackPath = null, int fallbackIndex = 0, string? fallbackPackageFullName = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		cancellationToken.ThrowIfCancellationRequested();

		var icon = TryGetAppExtensionIcon(path, index, size, cancellationToken, packageFullName);
		if (!icon.IsEmpty || string.IsNullOrWhiteSpace(fallbackPath))
		{
			return icon;
		}

		return TryGetAppExtensionIcon(fallbackPath, fallbackIndex, size, cancellationToken, fallbackPackageFullName);
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

	private static ReadOnlyMemory<byte> TryGetAppExtensionIcon(string path, int index, int size, CancellationToken cancellationToken, string? packageFullName)
	{
		var resolvedPath = ResolveAppExtensionIconPath(path, packageFullName);
		if (resolvedPath is null)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		if (Path.GetExtension(resolvedPath).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp")
		{
			try
			{
				return File.ReadAllBytes(resolvedPath);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				return ReadOnlyMemory<byte>.Empty;
			}
		}

		return GetResourceIcon(resolvedPath, index, size, cancellationToken);
	}

	private static string? ResolveAppExtensionIconPath(string path, string? packageFullName)
	{
		if (path.StartsWith("@{", StringComparison.Ordinal))
		{
			var buffer = new char[32768];
			if (PInvoke.SHLoadIndirectString(path, buffer).Failed)
			{
				return null;
			}

			path = buffer.AsSpan().TrimEnd('\0').ToString();
		}

		if (Path.IsPathFullyQualified(path))
		{
			return path;
		}

		if (Uri.TryCreate(path, UriKind.Absolute, out var fileUri) && fileUri.IsFile)
		{
			return fileUri.LocalPath;
		}

		if (string.IsNullOrWhiteSpace(packageFullName))
		{
			return null;
		}

		string relativePath;
		if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
		{
			if (!string.Equals(uri.Scheme, "ms-appx", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			relativePath = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/', '\\'));
		}
		else if (path.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		else
		{
			relativePath = path.TrimStart('/', '\\');
		}

		if (string.IsNullOrWhiteSpace(relativePath) || GetPackagePath(packageFullName) is not { Length: > 0 } packagePath)
		{
			return null;
		}

		try
		{
			var resolvedPath = Path.GetFullPath(Path.Combine(packagePath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
			var packageDirectoryPrefix = Path.EndsInDirectorySeparator(packagePath) ? packagePath : string.Concat(packagePath, Path.DirectorySeparatorChar);
			if (!resolvedPath.StartsWith(packageDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			return resolvedPath;
		}
		catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
		{
			return null;
		}
	}

	internal static string? GetPackagePath(string packageFullName)
	{
		uint pathLength = 0;
		if (PInvoke.GetPackagePathByFullName(packageFullName, ref pathLength, null) != ErrorInsufficientBuffer || pathLength is 0)
		{
			return null;
		}

		var pathBuffer = new char[checked((int)pathLength)];
		int result;
		fixed (char* path = pathBuffer)
		{
			result = PInvoke.GetPackagePathByFullName(packageFullName, ref pathLength, path);
		}

		return result is 0 ? new string(pathBuffer).TrimEnd('\0') : null;
	}
}
