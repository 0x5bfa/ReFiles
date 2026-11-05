// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

internal static class WindowsShellContextMenuIconSource
{
	internal static unsafe ReadOnlyMemory<byte> ReadCommandIcon(string canonicalName, IShellItemArray selection, object? site, int pixelSize)
	{
		var hr = PInvoke.CoCreateInstance(typeof(CommandStoreCoclass).GUID, null, CLSCTX.CLSCTX_INPROC_SERVER, out ICommandStore? store);
		if (hr.Failed || store is null)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		hr = store.GetCanonicalCommand(canonicalName, typeof(IExplorerCommand).GUID, out var command);
		if (hr.Failed || command is null)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		var objectWithSite = command as IObjectWithSite;
		PWSTR value = default;
		try
		{
			if (site is not null)
			{
				objectWithSite?.SetSite(site);
			}

			hr = command.GetIcon(selection, &value);
			if (hr.Failed || value.Value is null)
			{
				return ReadOnlyMemory<byte>.Empty;
			}

			var (path, index) = WindowsShellExplorerCommandSource.ParseCommandIcon(value.ToString());

			return path is null ? ReadOnlyMemory<byte>.Empty : ReadIcon(path, index, pixelSize, CancellationToken.None);
		}
		catch (COMException)
		{
			return ReadOnlyMemory<byte>.Empty;
		}
		finally
		{
			PInvoke.CoTaskMemFree(value.Value);
			objectWithSite?.SetSite(null!);
		}
	}
	internal static ReadOnlyMemory<byte> ReadIcon(string path, int index, int size, CancellationToken cancellationToken,
		string? packageFullName = null, string? fallbackPath = null, int fallbackIndex = 0, string? fallbackPackageFullName = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		cancellationToken.ThrowIfCancellationRequested();

		var icon = TryGetIcon(path, index, size, cancellationToken, packageFullName);
		if (!icon.IsEmpty || string.IsNullOrWhiteSpace(fallbackPath))
		{
			return icon;
		}

		return TryGetIcon(fallbackPath, fallbackIndex, size, cancellationToken, fallbackPackageFullName);
	}

	private static ReadOnlyMemory<byte> TryGetIcon(string path, int index, int size, CancellationToken cancellationToken, string? packageFullName)
	{
		var resolvedPath = ResolveIconPath(path, packageFullName);
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

		var hr = PInvoke.SHDefExtractIcon(resolvedPath, index, 0, out var largeIcon, out var smallIcon, checked((uint)(size | size << 16)));
		using (largeIcon)
		using (smallIcon)
		{
			return hr.Succeeded && !largeIcon.IsInvalid ? WindowsIconRenderer.EncodeHIcon(largeIcon, size, cancellationToken) ?? [] : ReadOnlyMemory<byte>.Empty;
		}
	}

	private static string? ResolveIconPath(string path, string? packageFullName)
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

		if (string.IsNullOrWhiteSpace(relativePath) || WindowsShellContextMenuAssets.GetPackagePathByFullName(packageFullName) is not { Length: > 0 } packagePath)
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

}
