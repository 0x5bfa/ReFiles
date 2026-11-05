// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Describes one initial or repeated FTP credential request.
/// </summary>
public sealed record FtpCredentialRequest
{
	/// <summary>Gets the connection profile.</summary>
	public FtpConnectionProfile Profile { get; }

	/// <summary>Gets a value indicating whether this is a retry.</summary>
	public bool IsRetry { get; }

	/// <summary>Initializes a credential request.</summary>
	/// <param name="profile">The connection profile.</param>
	/// <param name="isRetry">Whether a previous credential was rejected.</param>
	public FtpCredentialRequest(FtpConnectionProfile profile, bool isRetry)
	{
		ArgumentNullException.ThrowIfNull(profile);

		Profile = profile;
		IsRetry = isRetry;
	}
}
