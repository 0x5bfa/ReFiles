// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>Specifies the kind of an FTP directory entry.</summary>
public enum FtpEntryKind
{
	/// <summary>A regular file.</summary>
	File,
	/// <summary>A directory.</summary>
	Folder,
	/// <summary>A symbolic link.</summary>
	SymbolicLink,
}

/// <summary>
/// Contains source-neutral metadata copied from an FTP listing.
/// </summary>
public sealed record FtpEntryInfo
{
	/// <summary>Gets the normalized entry path.</summary>
	public FtpPath Path { get; }

	/// <summary>Gets the entry name.</summary>
	public string Name { get; }

	/// <summary>Gets the entry kind.</summary>
	public FtpEntryKind Kind { get; }

	/// <summary>Gets the entry size, when known.</summary>
	public long? Size { get; }

	/// <summary>Gets the last modification time, when known.</summary>
	public DateTimeOffset? DateModified { get; }

	/// <summary>Gets the creation time, when known.</summary>
	public DateTimeOffset? DateCreated { get; }

	/// <summary>Gets the symbolic link target, when applicable.</summary>
	public string? LinkTarget { get; }

	/// <summary>Initializes FTP entry metadata.</summary>
	/// <param name="path">The entry path.</param>
	/// <param name="name">The entry name.</param>
	/// <param name="kind">The entry kind.</param>
	/// <param name="size">The entry size.</param>
	/// <param name="dateModified">The modification time.</param>
	/// <param name="dateCreated">The creation time.</param>
	/// <param name="linkTarget">The symbolic link target.</param>
	public FtpEntryInfo(FtpPath path, string name, FtpEntryKind kind, long? size = null, DateTimeOffset? dateModified = null, DateTimeOffset? dateCreated = null, string? linkTarget = null)
	{
		ArgumentNullException.ThrowIfNull(path);
		ArgumentNullException.ThrowIfNull(name);

		if (kind is not FtpEntryKind.File and not FtpEntryKind.Folder and not FtpEntryKind.SymbolicLink)
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		if (size is < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(size));
		}

		Path = path;
		Name = name;
		Kind = kind;
		Size = size;
		DateModified = dateModified;
		DateCreated = dateCreated;
		LinkTarget = linkTarget;
	}
}
