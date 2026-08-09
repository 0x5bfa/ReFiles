// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Files.Infrastructure;

internal static class FileClipboard
{
	public static bool HasStorageItems
	{
		get
		{
			try
			{
				return Clipboard.GetContent().Contains(StandardDataFormats.StorageItems);
			}
			catch
			{
				return false;
			}
		}
	}

	public static async Task SetStorageItemsAsync(IReadOnlyList<string> paths, bool move, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(paths);

		var storageItems = new List<IStorageItem>(paths.Count);
		foreach (var path in paths)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (Directory.Exists(path))
			{
				storageItems.Add(await StorageFolder.GetFolderFromPathAsync(path));
			}
			else if (File.Exists(path))
			{
				storageItems.Add(await StorageFile.GetFileFromPathAsync(path));
			}
			else
			{
				throw new FileNotFoundException("The clipboard source item no longer exists.", path);
			}
		}

		if (storageItems.Count is 0)
		{
			throw new InvalidOperationException("The clipboard cannot contain an empty item set.");
		}

		var package = new DataPackage
		{
			RequestedOperation = move ? DataPackageOperation.Move : DataPackageOperation.Copy,
		};
		package.SetStorageItems(storageItems);
		Clipboard.SetContent(package);
		Clipboard.Flush();
	}

	public static async Task<FileClipboardContent?> GetStorageItemsAsync(CancellationToken cancellationToken = default)
	{
		var data = Clipboard.GetContent();
		if (!data.Contains(StandardDataFormats.StorageItems))
		{
			return null;
		}

		var storageItems = await data.GetStorageItemsAsync();
		var paths = new List<string>(storageItems.Count);
		foreach (var storageItem in storageItems)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				if (!string.IsNullOrWhiteSpace(storageItem.Path))
				{
					paths.Add(storageItem.Path);
				}
			}
			catch
			{
			}
		}

		if (paths.Count is 0)
		{
			return null;
		}

		return new FileClipboardContent(paths, data.RequestedOperation);
	}
}

internal sealed record FileClipboardContent(IReadOnlyList<string> Paths, DataPackageOperation RequestedOperation);
