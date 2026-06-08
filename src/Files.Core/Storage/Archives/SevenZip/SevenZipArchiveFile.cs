// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using OwlCore.Storage;

namespace Files.Core.Storage.Archives.SevenZip;

internal sealed class SevenZipArchiveFile
	: SevenZipArchiveStorable, IChildFile
{
	public SevenZipArchiveFile(
		SevenZipArchiveMount mount,
		SevenZipArchiveNode node)
		: base(mount, node)
	{
		if (node.IsDirectory || node.EntryIndex is null)
		{
			throw new ArgumentException(
				"An archive file requires an indexed file entry.",
				nameof(node));
		}
	}

	public Task<Stream> OpenStreamAsync(
		FileAccess accessMode,
		CancellationToken cancellationToken = default)
	{
		if (accessMode is not FileAccess.Read)
		{
			throw new NotSupportedException(
				"Archive entries are read-only.");
		}

		return Mount.OpenFileAsync(
			Node,
			cancellationToken);
	}
}
