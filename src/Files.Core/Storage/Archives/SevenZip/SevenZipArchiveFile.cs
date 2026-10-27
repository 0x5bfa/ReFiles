// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Capabilities.Content;
using Files.Core.Storage;

namespace Files.Core.Storage.Archives.SevenZip;

internal sealed class SevenZipArchiveFile
	: SevenZipArchiveStorable, IFile, IReadableContent
{
	public SevenZipArchiveFile(SevenZipArchiveMount mount, SevenZipArchiveNode node)
		: base(mount, node)
	{
		if (node.IsDirectory || node.EntryIndex is null)
		{
			throw new ArgumentException("An archive file requires an indexed file entry.", nameof(node));
		}
	}

	public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
	{
		return Mount.OpenFileAsync(Node, cancellationToken);
	}
}
