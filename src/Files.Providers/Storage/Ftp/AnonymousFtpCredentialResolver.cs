// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Resolves conventional anonymous FTP credentials.
/// </summary>
public sealed class AnonymousFtpCredentialResolver : IFtpCredentialResolver
{
	/// <summary>Gets the shared anonymous credential resolver.</summary>
	public static AnonymousFtpCredentialResolver Instance { get; } = new();

	private AnonymousFtpCredentialResolver()
	{
	}

	/// <summary>Resolves the conventional anonymous FTP credential.</summary>
	/// <param name="request">The credential request.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The anonymous credential.</returns>
	public ValueTask<FtpCredential?> ResolveAsync(FtpCredentialRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult<FtpCredential?>(new FtpCredential("anonymous", "anonymous@"));
	}
}
