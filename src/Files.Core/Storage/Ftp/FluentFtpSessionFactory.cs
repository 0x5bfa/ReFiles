// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using FluentFTP;
using FluentFTP.Exceptions;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Opens FluentFTP-backed sessions with platform certificate validation.
/// </summary>
public sealed class FluentFtpSessionFactory : IFtpSessionFactory
{
	public static FluentFtpSessionFactory Instance { get; } = new();

	public async ValueTask<IFtpSession> ConnectAsync(FtpConnectionProfile profile, FtpCredential credential, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(profile);
		ArgumentNullException.ThrowIfNull(credential);
		cancellationToken.ThrowIfCancellationRequested();

		var config = new FtpConfig
		{
			EncryptionMode = profile.SecurityMode switch
			{
				FtpSecurityMode.Plain => FtpEncryptionMode.None,
				FtpSecurityMode.ExplicitTls => FtpEncryptionMode.Explicit,
				FtpSecurityMode.ImplicitTls => FtpEncryptionMode.Implicit,
				_ => throw new ArgumentOutOfRangeException(nameof(profile), "Unsupported FTP security mode."),
			},
		};
		var client = new AsyncFtpClient(profile.Host, new NetworkCredential(credential.UserName, credential.Password), profile.Port, config);

		try
		{
			await client.Connect(cancellationToken).ConfigureAwait(false);

			return new FluentFtpSession(client, profile.PathComparer);
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			await TryDisposeAsync(client).ConfigureAwait(false);
			throw;
		}
		catch (FtpAuthenticationException exception)
		{
			var cleanupError = await TryDisposeAsync(client).ConfigureAwait(false);
			throw new FtpAuthenticationRequiredException(
				profile.ConnectionId,
				$"Authentication failed for FTP connection '{profile.DisplayName}'.",
				cleanupError is null
					? exception
					: new AggregateException("FTP authentication and connection cleanup both failed.", exception, cleanupError));
		}
		catch (Exception connectionError)
		{
			var cleanupError = await TryDisposeAsync(client).ConfigureAwait(false);
			if (cleanupError is not null)
			{
				throw new AggregateException("FTP connection and cleanup both failed.", connectionError, cleanupError);
			}

			throw;
		}
	}

	private static async ValueTask<Exception?> TryDisposeAsync(AsyncFtpClient client)
	{
		try
		{
			await client.DisposeAsync().ConfigureAwait(false);

			return null;
		}
		catch (Exception exception)
		{
			return exception;
		}
	}
}
