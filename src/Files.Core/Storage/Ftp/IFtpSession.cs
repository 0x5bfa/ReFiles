// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Provides one connected, non-concurrent FTP command session.
/// </summary>
public interface IFtpSession : IAsyncDisposable
{
	ValueTask<FtpEntryInfo?> GetEntryAsync(FtpPath path, CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyList<FtpEntryInfo>> GetListingAsync(FtpPath path, CancellationToken cancellationToken = default);

	ValueTask<Stream> OpenReadAsync(FtpPath path, CancellationToken cancellationToken = default);

	ValueTask<Stream> OpenWriteAsync(FtpPath path, CancellationToken cancellationToken = default);

	ValueTask CompleteTransferAsync(CancellationToken cancellationToken = default);

	ValueTask CreateFileAsync(FtpPath path, CancellationToken cancellationToken = default);

	ValueTask CreateFolderAsync(FtpPath path, CancellationToken cancellationToken = default);

	ValueTask DeleteAsync(FtpPath path, FtpEntryKind kind, CancellationToken cancellationToken = default);

	ValueTask MoveAsync(FtpPath sourcePath, FtpPath destinationPath, FtpEntryKind kind, CancellationToken cancellationToken = default);
}
