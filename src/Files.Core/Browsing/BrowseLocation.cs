// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.Core.Browsing;

/// <summary>Identifies a location that can be opened for browsing.</summary>
public abstract record BrowseLocation;

/// <summary>Identifies a folder location.</summary>
public sealed record FolderLocation : BrowseLocation
{
	/// <summary>Gets the folder reference.</summary>
	public StorableReference Folder { get; }

	/// <summary>Initializes a folder location.</summary>
	/// <param name="folder">The folder reference.</param>
	public FolderLocation(StorableReference folder)
	{
		ArgumentNullException.ThrowIfNull(folder);

		Folder = folder;
	}
}

/// <summary>Identifies an entry inside an archive.</summary>
public sealed record ArchiveLocation : BrowseLocation
{
	/// <summary>Gets the archive reference.</summary>
	public StorableReference Archive { get; }

	/// <summary>Gets the normalized path of the archive entry.</summary>
	public string EntryPath { get; }

	/// <summary>Initializes an archive location.</summary>
	/// <param name="archive">The archive reference.</param>
	/// <param name="entryPath">The optional path of the entry inside the archive.</param>
	public ArchiveLocation(StorableReference archive, string? entryPath = null)
	{
		ArgumentNullException.ThrowIfNull(archive);

		Archive = archive;
		EntryPath = ArchiveEntryPath.Normalize(entryPath);
	}

	/// <summary>Initializes an archive location from an archive entry.</summary>
	/// <param name="entry">The archive entry.</param>
	public ArchiveLocation(IArchiveEntry entry)
		: this(entry?.Archive ?? throw new ArgumentNullException(nameof(entry)), entry?.EntryPath)
	{
	}
}

/// <summary>Identifies the user's home location.</summary>
public sealed record HomeLocation : BrowseLocation
{
	/// <summary>Gets the shared home location instance.</summary>
	public static HomeLocation Instance { get; } = new();

	private HomeLocation()
	{
	}
}

/// <summary>Identifies a search query and its optional scope.</summary>
public sealed record SearchLocation : BrowseLocation
{
	/// <summary>Gets the query text.</summary>
	public string Query { get; }

	/// <summary>Gets the optional location used to scope the search.</summary>
	public StorableReference? Scope { get; }

	/// <summary>Initializes a search location.</summary>
	/// <param name="query">The query text.</param>
	/// <param name="scope">The optional search scope.</param>
	public SearchLocation(string query, StorableReference? scope = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);

		Query = query;
		Scope = scope;
	}
}

/// <summary>Identifies items associated with a tag.</summary>
public sealed record TagLocation : BrowseLocation
{
	/// <summary>Gets the tag identifier.</summary>
	public string TagId { get; }

	/// <summary>Initializes a tag location.</summary>
	/// <param name="tagId">The tag identifier.</param>
	public TagLocation(string tagId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tagId);

		TagId = tagId;
	}
}
