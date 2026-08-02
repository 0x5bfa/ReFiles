// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.Core.Browsing;

public abstract record BrowseLocation;

public sealed record FolderLocation : BrowseLocation
{
	public StorableReference Folder { get; }

	public FolderLocation(StorableReference folder)
	{
		ArgumentNullException.ThrowIfNull(folder);

		Folder = folder;
	}
}

public sealed record ArchiveLocation : BrowseLocation
{
	public StorableReference Archive { get; }

	public string EntryPath { get; }

	public ArchiveLocation(StorableReference archive, string? entryPath = null)
	{
		ArgumentNullException.ThrowIfNull(archive);

		Archive = archive;
		EntryPath = ArchiveEntryPath.Normalize(entryPath);
	}

	public ArchiveLocation(IArchiveEntry entry)
		: this(entry?.Archive ?? throw new ArgumentNullException(nameof(entry)), entry?.EntryPath)
	{
	}
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
	public string Query { get; }

	public StorableReference? Scope { get; }

	public SearchLocation(string query, StorableReference? scope = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);

		Query = query;
		Scope = scope;
	}
}

public sealed record TagLocation : BrowseLocation
{
	public string TagId { get; }

	public TagLocation(string tagId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tagId);

		TagId = tagId;
	}
}
