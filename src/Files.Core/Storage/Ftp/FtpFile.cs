// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Capabilities.Content;
using Files.Core.Storage;

namespace Files.Core.Storage.Ftp;

/// <summary>Represents a file exposed by an FTP source.</summary>
public sealed class FtpFile : FtpStorable, IFile, IReadableContent, IWritableContent
{
	internal FtpFile(FtpStorageSource source, FtpStorableSnapshot snapshot, FtpStorableFactory factory)
		: base(source, snapshot, factory)
	{
	}

	/// <summary>Opens an owned read stream that retains its FTP session until disposal.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The stream owned by the caller, independently of this item and source.</returns>
	public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return await Factory.OpenReadAsync(Path, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Opens an owned write stream using the FTP session's overwrite behavior.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The stream owned by the caller, retaining its independent FTP session until disposal.</returns>
	public async Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return await Factory.OpenWriteAsync(Path, cancellationToken).ConfigureAwait(false);
	}
}
