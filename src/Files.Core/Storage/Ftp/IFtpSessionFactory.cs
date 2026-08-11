// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Opens authenticated FTP sessions for one source.
/// </summary>
public interface IFtpSessionFactory
{
	/// <summary>Connects an FTP session.</summary>
	/// <param name="profile">The connection profile.</param>
	/// <param name="credential">The connection credential.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The connected session.</returns>
	ValueTask<IFtpSession> ConnectAsync(FtpConnectionProfile profile, FtpCredential credential, CancellationToken cancellationToken = default);
}
