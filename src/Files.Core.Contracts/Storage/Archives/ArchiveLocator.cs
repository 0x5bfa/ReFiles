// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Describes how to reopen current archive content without retaining a live mount.</summary>
public sealed record ArchiveLocator
{
	private const int MaximumDepth = 32;
	private readonly int _depth = 1;

	/// <summary>Gets the outermost item in a configured storage source.</summary>
	public StorableReference Root { get; }

	/// <summary>Gets the containing archive, or <see langword="null"/> for the outermost archive.</summary>
	public ArchiveLocator? Parent { get; }

	/// <summary>Gets the containing archive's record identifier for a nested archive.</summary>
	public string? EntryId { get; }

	/// <summary>Gets the expected decoded path used to reject a changed record selection.</summary>
	public string? EntryPath { get; }

	/// <summary>Creates a locator for an item in a configured storage source.</summary>
	/// <param name="root">The outermost archive reference.</param>
	public ArchiveLocator(StorableReference root)
	{
		ArgumentNullException.ThrowIfNull(root);

		Root = root;
	}

	private ArchiveLocator(ArchiveLocator parent, string entryId, string entryPath)
	{
		if (parent._depth >= MaximumDepth)
		{
			throw new ArgumentException("Archive nesting exceeds the supported depth.", nameof(parent));
		}

		_depth = parent._depth + 1;
		Root = parent.Root;
		Parent = parent;
		EntryId = entryId;
		EntryPath = entryPath;
	}

	/// <summary>Appends an exact record selection for a nested archive.</summary>
	/// <param name="entryId">The containing reader's record identifier.</param>
	/// <param name="entryPath">The expected decoded record path.</param>
	/// <returns>The nested archive locator.</returns>
	public ArchiveLocator Append(string entryId, string entryPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

		ArgumentNullException.ThrowIfNull(entryPath);

		return new ArchiveLocator(this, entryId, ArchiveEntryPath.Normalize(entryPath));
	}
}
