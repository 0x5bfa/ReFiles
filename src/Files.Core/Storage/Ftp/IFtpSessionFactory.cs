// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Opens authenticated FTP sessions for one source.
/// </summary>
public interface IFtpSessionFactory
{
	ValueTask<IFtpSession> ConnectAsync(FtpConnectionProfile profile, FtpCredential credential, CancellationToken cancellationToken = default);
}
