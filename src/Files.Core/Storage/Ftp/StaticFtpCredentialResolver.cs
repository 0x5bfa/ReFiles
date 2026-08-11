// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Resolves one credential from application-owned memory.
/// </summary>
public sealed class StaticFtpCredentialResolver : IFtpCredentialResolver
{
	private readonly FtpCredential _credential;

	/// <summary>Initializes a resolver with one credential.</summary>
	/// <param name="credential">The credential to return.</param>
	public StaticFtpCredentialResolver(FtpCredential credential)
	{
		ArgumentNullException.ThrowIfNull(credential);

		_credential = credential;
	}

	/// <summary>Returns the configured credential.</summary>
	/// <param name="request">The credential request.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The configured credential.</returns>
	public ValueTask<FtpCredential?> ResolveAsync(FtpCredentialRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult<FtpCredential?>(_credential);
	}
}
