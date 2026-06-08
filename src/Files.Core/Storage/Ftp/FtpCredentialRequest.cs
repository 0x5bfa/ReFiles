// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Describes one initial or repeated FTP credential request.
/// </summary>
public sealed record FtpCredentialRequest
{
	public FtpCredentialRequest(
		FtpConnectionProfile profile,
		bool isRetry)
	{
		ArgumentNullException.ThrowIfNull(profile);
		Profile = profile;
		IsRetry = isRetry;
	}

	public FtpConnectionProfile Profile { get; }

	public bool IsRetry { get; }
}
