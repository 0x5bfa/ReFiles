// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.Core.Browsing;

public abstract record BrowseLocation;

public sealed record FolderLocation : BrowseLocation
{
	public FolderLocation(StorableReference folder)
	{
		ArgumentNullException.ThrowIfNull(folder);
		Folder = folder;
	}

	public StorableReference Folder { get; }
}

public sealed record ArchiveLocation : BrowseLocation
{
	public ArchiveLocation(
		StorableReference archive,
		string? entryPath = null)
	{
		ArgumentNullException.ThrowIfNull(archive);

		Archive = archive;
		EntryPath = ArchiveEntryPath.Normalize(entryPath);
	}

	public ArchiveLocation(IArchiveEntry entry)
		: this(
			entry?.Archive
				?? throw new ArgumentNullException(nameof(entry)),
			entry?.EntryPath)
	{
	}

	public StorableReference Archive { get; }

	public string EntryPath { get; }
}

public sealed record HomeLocation : BrowseLocation
{
	public static HomeLocation Instance { get; } = new();

	private HomeLocation()
	{
	}
}

public sealed record SearchLocation : BrowseLocation
{
	public SearchLocation(string query, StorableReference? scope = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);
		Query = query;
		Scope = scope;
	}

	public string Query { get; }

	public StorableReference? Scope { get; }
}

public sealed record TagLocation : BrowseLocation
{
	public TagLocation(string tagId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
		TagId = tagId;
	}

	public string TagId { get; }
}
