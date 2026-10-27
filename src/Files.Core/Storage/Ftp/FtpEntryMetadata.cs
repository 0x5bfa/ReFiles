// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Contains network-independent metadata copied from an FTP response.
/// </summary>
internal sealed record FtpEntryMetadata(FtpPath Path, string Name, FtpEntryKind Kind, long? Size, DateTimeOffset? DateModified, DateTimeOffset? DateCreated, string? LinkTarget)
{
	public static FtpEntryMetadata FromEntry(FtpEntryInfo entry)
	{
		ArgumentNullException.ThrowIfNull(entry);

		return new FtpEntryMetadata(entry.Path, entry.Name, entry.Kind, entry.Size, entry.DateModified, entry.DateCreated, entry.LinkTarget);
	}
}
