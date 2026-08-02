// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using OwlCore.Storage;

namespace Files.Core.Storage.Ftp;

public sealed class FtpFile : FtpStorable, IChildFile
{
	internal FtpFile(FtpStorageSource source, FtpStorableSnapshot snapshot, FtpStorableFactory factory)
		: base(source, snapshot, factory)
	{
	}

	public async Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return accessMode switch
		{
			FileAccess.Read =>
				await Factory
					.OpenReadAsync(Path, cancellationToken)
					.ConfigureAwait(false),
			FileAccess.Write =>
				await Factory
					.OpenWriteAsync(Path, cancellationToken)
					.ConfigureAwait(false),
			FileAccess.ReadWrite =>
				throw new NotSupportedException("FTP does not expose one bidirectional file stream."),
			_ => throw new ArgumentOutOfRangeException(nameof(accessMode)),
		};
	}
}
