// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Contains network-independent metadata copied from an FTP response.
/// </summary>
internal sealed record FtpStorableSnapshot(
	FtpPath Path,
	string Name,
	FtpEntryKind Kind,
	long? Size,
	DateTimeOffset? DateModified,
	DateTimeOffset? DateCreated,
	string? LinkTarget)
{
	public static FtpStorableSnapshot FromEntry(
		FtpEntryInfo entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		return new FtpStorableSnapshot(
			entry.Path,
			entry.Name,
			entry.Kind,
			entry.Size,
			entry.DateModified,
			entry.DateCreated,
			entry.LinkTarget);
	}
}
