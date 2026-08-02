// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Resolves one credential from application-owned memory.
/// </summary>
public sealed class StaticFtpCredentialResolver : IFtpCredentialResolver
{
	private readonly FtpCredential credential;

	public StaticFtpCredentialResolver(FtpCredential credential)
	{
		ArgumentNullException.ThrowIfNull(credential);
		this.credential = credential;
	}

	public ValueTask<FtpCredential?> ResolveAsync(FtpCredentialRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult<FtpCredential?>(credential);
	}
}
