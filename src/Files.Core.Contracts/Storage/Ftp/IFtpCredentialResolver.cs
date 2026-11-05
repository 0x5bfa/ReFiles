// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Resolves credentials without storing secrets in a connection profile.
/// </summary>
public interface IFtpCredentialResolver
{
	/// <summary>Resolves credentials for an FTP connection request.</summary>
	/// <param name="request">The credential request.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The credential, or <see langword="null"/> when none is available.</returns>
	ValueTask<FtpCredential?> ResolveAsync(FtpCredentialRequest request, CancellationToken cancellationToken = default);
}
