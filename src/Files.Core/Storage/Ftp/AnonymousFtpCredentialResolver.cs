// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Resolves conventional anonymous FTP credentials.
/// </summary>
public sealed class AnonymousFtpCredentialResolver : IFtpCredentialResolver
{
	public static AnonymousFtpCredentialResolver Instance { get; } = new();

	private AnonymousFtpCredentialResolver()
	{
	}

	public ValueTask<FtpCredential?> ResolveAsync(
		FtpCredentialRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult<FtpCredential?>(
			new FtpCredential("anonymous", "anonymous@"));
	}
}
