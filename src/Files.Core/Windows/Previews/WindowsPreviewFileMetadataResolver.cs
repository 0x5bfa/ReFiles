// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using System.Security;
using Files.Core.Capabilities;
using OwlCore.Storage;
using Windows.Win32;

namespace Files.Core.Windows;

internal sealed class WindowsPreviewFileMetadataResolver : IWindowsPreviewFileMetadataResolver
{
	private const uint FileAttributeDirectory = 0x00000010;
	public WindowsPreviewFileMetadata? GetMetadata(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.CoreModel is not IWindowsStorable item || context.CoreModel is not IFile || string.IsNullOrWhiteSpace(item.FileSystemPath) || !Path.IsPathFullyQualified(item.FileSystemPath))
		{
			return null;
		}

		try
		{
			var attributes = PInvoke.GetFileAttributes(item.FileSystemPath);
			if (attributes == PInvoke.INVALID_FILE_ATTRIBUTES || (attributes & FileAttributeDirectory) != 0)
			{
				return null;
			}

			var fileInfo = new FileInfo(item.FileSystemPath);
			fileInfo.Refresh();
			if (!fileInfo.Exists)
			{
				return null;
			}

			return new WindowsPreviewFileMetadata(attributes, fileInfo.Length);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or SecurityException or NotSupportedException)
		{
			return null;
		}
	}
}
