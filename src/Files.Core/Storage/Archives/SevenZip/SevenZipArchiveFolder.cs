// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using OwlCore.Storage;

namespace Files.Core.Storage.Archives.SevenZip;

internal sealed class SevenZipArchiveFolder
	: SevenZipArchiveStorable, IChildFolder
{
	public SevenZipArchiveFolder(
		SevenZipArchiveMount mount,
		SevenZipArchiveNode node)
		: base(mount, node)
	{
		if (!node.IsDirectory)
		{
			throw new ArgumentException(
				"An archive folder requires a directory entry.",
				nameof(node));
		}
	}

	public async IAsyncEnumerable<IStorableChild> GetItemsAsync(
		StorableType type = StorableType.All,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await Task.CompletedTask.ConfigureAwait(false);

		foreach (var child in Mount.GetChildren(Node.Path))
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (child.IsDirectory
				&& type.HasFlag(StorableType.Folder))
			{
				yield return Mount.CreateFolder(child.Path);
			}
			else if (!child.IsDirectory
				&& type.HasFlag(StorableType.File))
			{
				yield return Mount.CreateFile(child.Path);
			}
		}
	}
}
