// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Provides one connected, non-concurrent FTP command session.
/// </summary>
public interface IFtpSession : IAsyncDisposable
{
	/// <summary>Gets metadata for an FTP path.</summary>
	/// <param name="path">The path to inspect.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The entry metadata, or <see langword="null"/> when the path does not exist.</returns>
	ValueTask<FtpEntryInfo?> GetEntryAsync(FtpPath path, CancellationToken cancellationToken = default);

	/// <summary>Gets the entries in an FTP folder.</summary>
	/// <param name="path">The folder path.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The folder entries.</returns>
	ValueTask<IReadOnlyList<FtpEntryInfo>> GetListingAsync(FtpPath path, CancellationToken cancellationToken = default);

	/// <summary>Opens an FTP path for reading.</summary>
	/// <param name="path">The file path.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A readable stream.</returns>
	ValueTask<Stream> OpenReadAsync(FtpPath path, CancellationToken cancellationToken = default);

	/// <summary>Opens an FTP path for writing.</summary>
	/// <param name="path">The file path.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A writable stream.</returns>
	ValueTask<Stream> OpenWriteAsync(FtpPath path, CancellationToken cancellationToken = default);

	/// <summary>Completes the current upload transfer.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask CompleteTransferAsync(CancellationToken cancellationToken = default);

	/// <summary>Creates an empty file.</summary>
	/// <param name="path">The file path.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask CreateFileAsync(FtpPath path, CancellationToken cancellationToken = default);

	/// <summary>Creates a folder.</summary>
	/// <param name="path">The folder path.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask CreateFolderAsync(FtpPath path, CancellationToken cancellationToken = default);

	/// <summary>Deletes an FTP entry.</summary>
	/// <param name="path">The entry path.</param>
	/// <param name="kind">The entry kind.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask DeleteAsync(FtpPath path, FtpEntryKind kind, CancellationToken cancellationToken = default);

	/// <summary>Moves an FTP entry.</summary>
	/// <param name="sourcePath">The source path.</param>
	/// <param name="destinationPath">The destination path.</param>
	/// <param name="kind">The entry kind.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask MoveAsync(FtpPath sourcePath, FtpPath destinationPath, FtpEntryKind kind, CancellationToken cancellationToken = default);
}
