// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Storage.Archives.SevenZip;

internal abstract class SevenZipArchiveStorable
	: IStorableChild, IArchiveEntry, IStorageAddressSource
{
	protected SevenZipArchiveStorable(SevenZipArchiveMount mount, SevenZipArchiveNode node)
	{
		ArgumentNullException.ThrowIfNull(mount);
		ArgumentNullException.ThrowIfNull(node);

		Mount = mount;
		Node = node;
		Id = string.IsNullOrEmpty(node.Path)
			? "/"
			: node.Path;
		Name = node.Name;
		Address = new StorageAddress(SevenZipArchiveMount.EntryAddressScheme, Id);
	}

	protected SevenZipArchiveMount Mount { get; }

	protected SevenZipArchiveNode Node { get; }

	public string Id { get; }

	public string Name { get; }

	public StorageAddress Address { get; }

	public StorableReference Archive => Mount.Archive;

	public string EntryPath => Node.Path;

	public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (string.IsNullOrEmpty(Node.Path))
		{
			return Task.FromResult<IFolder?>(null);
		}

		var parentPath = ArchiveEntryPath.GetParent(Node.Path);
		return Task.FromResult<IFolder?>(Mount.CreateFolder(parentPath));
	}
}
