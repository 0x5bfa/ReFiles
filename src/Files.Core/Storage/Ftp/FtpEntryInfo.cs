// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

public enum FtpEntryKind
{
	File,
	Folder,
	SymbolicLink,
}

/// <summary>
/// Contains source-neutral metadata copied from an FTP listing.
/// </summary>
public sealed record FtpEntryInfo
{
	public FtpEntryInfo(
		FtpPath path,
		string name,
		FtpEntryKind kind,
		long? size = null,
		DateTimeOffset? dateModified = null,
		DateTimeOffset? dateCreated = null,
		string? linkTarget = null)
	{
		ArgumentNullException.ThrowIfNull(path);
		ArgumentNullException.ThrowIfNull(name);
		if (kind is not FtpEntryKind.File
			and not FtpEntryKind.Folder
			and not FtpEntryKind.SymbolicLink)
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

	public FtpPath Path { get; }

	public string Name { get; }

	public FtpEntryKind Kind { get; }

	public long? Size { get; }

	public DateTimeOffset? DateModified { get; }

	public DateTimeOffset? DateCreated { get; }

	public string? LinkTarget { get; }
}
