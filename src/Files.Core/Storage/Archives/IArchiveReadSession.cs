// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Archives;

/// <summary>Owns a reader for one opened archive. The input stream is borrowed from its owner.</summary>
public interface IArchiveReadSession : IAsyncDisposable
{
	/// <summary>Gets the identifier of the reader that opened this session.</summary>
	string BackendId { get; }

	/// <summary>Gets every physical record, including duplicate and unprojectable paths.</summary>
	IReadOnlyList<ArchiveEntryInfo> Entries { get; }

	/// <summary>Reads and verifies one record into an independent, owned, seekable stream.</summary>
	/// <param name="entryId">The exact physical record identifier.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>A verified stream owned by the caller that can outlive the session.</returns>
	Task<Stream> OpenReadAsync(string entryId, CancellationToken cancellationToken = default);
}
