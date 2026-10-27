// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Archives;

namespace Files.Core.Storage;

/// <summary>Identifies one physical archive record, including records with duplicate names.</summary>
public sealed record ArchiveRecordLocator : StorageItemLocator
{
	/// <summary>Gets the containing archive chain.</summary>
	public ArchiveLocator Container { get; }
	/// <summary>Gets the exact physical record identifier.</summary>
	public string RecordId { get; }
	/// <summary>Gets the expected normalized record path.</summary>
	public string EntryPath { get; }
	/// <inheritdoc />
	public override StorableReference Root => Container.Root;

	/// <summary>Creates a record locator for current-content resolution.</summary>
	/// <param name="container">The containing archive chain.</param>
	/// <param name="recordId">The exact physical record identifier.</param>
	/// <param name="entryPath">The expected decoded path.</param>
	public ArchiveRecordLocator(ArchiveLocator container, string recordId, string entryPath)
	{
		ArgumentNullException.ThrowIfNull(container);

		ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

		ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);

		Container = container;
		RecordId = recordId;
		EntryPath = ArchiveEntryPath.Normalize(entryPath);
	}
}
