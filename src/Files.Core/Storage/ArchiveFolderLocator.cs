// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Archives;

namespace Files.Core.Storage;

/// <summary>Identifies a logical archive folder; an empty path selects the archive contents.</summary>
public sealed record ArchiveFolderLocator : StorageItemLocator
{
	/// <summary>Gets the containing archive chain.</summary>
	public ArchiveLocator Container { get; }
	/// <summary>Gets the normalized logical folder path.</summary>
	public string EntryPath { get; }
	/// <inheritdoc />
	public override StorableReference Root => Container.Root;

	/// <summary>Creates a logical archive folder locator.</summary>
	/// <param name="container">The containing archive chain.</param>
	/// <param name="entryPath">The folder path within the archive.</param>
	public ArchiveFolderLocator(ArchiveLocator container, string entryPath)
	{
		ArgumentNullException.ThrowIfNull(container);

		ArgumentNullException.ThrowIfNull(entryPath);

		Container = container;
		EntryPath = ArchiveEntryPath.Normalize(entryPath);
	}
}
