// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Threading;
using System.Threading.Tasks;
using Files.Core.Storage.Ftp;

namespace Files.BackgroundServer;

internal sealed class ServerFtpCredentialResolver(FtpCredential credential) : IFtpCredentialResolver
{
	private FtpCredential _credential = credential;
	internal FtpCredential Credential => Volatile.Read(ref _credential);

	internal void Update(FtpCredential credential)
	{
		Volatile.Write(ref _credential, credential);
	}

	public ValueTask<FtpCredential?> ResolveAsync(FtpCredentialRequest request, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult<FtpCredential?>(Credential);
	}
}
