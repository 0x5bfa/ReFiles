// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.Storage.Archives;

namespace Files.Core.Storage;

/// <summary>Captures a transfer source without retaining a live model or archive session.</summary>
public abstract record StorageItemLocator
{
	/// <summary>Gets the configured item at the root of this locator.</summary>
	public abstract StorableReference Root { get; }

	/// <summary>Captures the current selection's provenance for execution by another owner.</summary>
	/// <param name="item">The borrowed selected model.</param>
	/// <returns>The source locator, independent of the model's lifetime.</returns>
	public static StorageItemLocator Capture(IStorableModel item)
	{
		ArgumentNullException.ThrowIfNull(item);

		return item.GetCoreModel() is IArchiveEntry entry
			? item is IFolderModel ? new ArchiveFolderLocator(entry.Locator, entry.EntryPath) : new ArchiveRecordLocator(entry.Locator, entry.EntryId, entry.EntryPath)
			: new ConfiguredItemLocator(item.Reference);
	}
}
