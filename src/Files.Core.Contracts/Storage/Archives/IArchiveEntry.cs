// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>
/// Identifies an item inside an archive independently of its active backend.
/// </summary>
public interface IArchiveEntry
{
	/// <summary>Gets the locator of the archive containing this item.</summary>
	ArchiveLocator Locator { get; }

	/// <summary>Gets the physical record identifier, or a projected folder identifier.</summary>
	string EntryId { get; }

	/// <summary>Gets the archive reference.</summary>
	StorableReference Archive { get; }

	/// <summary>Gets the normalized entry path.</summary>
	string EntryPath { get; }
}
