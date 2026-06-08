// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Resolves credentials without storing secrets in a connection profile.
/// </summary>
public interface IFtpCredentialResolver
{
	ValueTask<FtpCredential?> ResolveAsync(
		FtpCredentialRequest request,
		CancellationToken cancellationToken = default);
}
