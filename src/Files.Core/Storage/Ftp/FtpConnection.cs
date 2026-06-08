// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Coordinates credentials and short-lived FTP sessions for one source.
/// </summary>
internal sealed class FtpConnection : IAsyncDisposable
{
	private readonly FtpConnectionProfile profile;
	private readonly IFtpCredentialResolver credentialResolver;
	private readonly IFtpSessionFactory sessionFactory;
	private readonly SemaphoreSlim credentialLock = new(1, 1);
	private FtpCredential? credential;
	private int isDisposed;

	public FtpConnection(
		FtpConnectionProfile profile,
		IFtpCredentialResolver credentialResolver,
		IFtpSessionFactory sessionFactory)
	{
		ArgumentNullException.ThrowIfNull(profile);
		ArgumentNullException.ThrowIfNull(credentialResolver);
		ArgumentNullException.ThrowIfNull(sessionFactory);

		this.profile = profile;
		this.credentialResolver = credentialResolver;
		this.sessionFactory = sessionFactory;
	}

	public async ValueTask<T> ExecuteAsync<T>(
		Func<IFtpSession, ValueTask<T>> operation,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(operation);

		await using var session = await OpenSessionAsync(
			cancellationToken).ConfigureAwait(false);
		return await operation(session).ConfigureAwait(false);
	}

	public async ValueTask ExecuteAsync(
		Func<IFtpSession, ValueTask> operation,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(operation);

		await using var session = await OpenSessionAsync(
			cancellationToken).ConfigureAwait(false);
		await operation(session).ConfigureAwait(false);
	}

	public ValueTask<Stream> OpenReadAsync(
		FtpPath path,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		return OpenStreamAsync(
			session => session.OpenReadAsync(path, cancellationToken),
			cancellationToken);
	}

	public ValueTask<Stream> OpenWriteAsync(
		FtpPath path,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		return OpenStreamAsync(
			session => session.OpenWriteAsync(path, cancellationToken),
			cancellationToken);
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) is 0)
		{
			credential = null;
		}

		GC.SuppressFinalize(this);
		return ValueTask.CompletedTask;
	}

	private async ValueTask<Stream> OpenStreamAsync(
		Func<IFtpSession, ValueTask<Stream>> openStream,
		CancellationToken cancellationToken)
	{
		ThrowIfDisposed();

		var session = await OpenSessionAsync(
			cancellationToken).ConfigureAwait(false);
		try
		{
			var stream = await openStream(session).ConfigureAwait(false);
			return new FtpOwnedStream(stream, session);
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			try
			{
				await session.DisposeAsync().ConfigureAwait(false);
			}
			catch
			{
				// Preserve cancellation.
			}

			throw;
		}
		catch (Exception openError)
		{
			try
			{
				await session.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException(
					"FTP stream opening and session cleanup both failed.",
					openError,
					cleanupError);
			}

			throw;
		}
	}

	private async ValueTask<IFtpSession> OpenSessionAsync(
		CancellationToken cancellationToken)
	{
		var currentCredential = await ResolveCredentialAsync(
			isRetry: false,
			rejectedCredential: null,
			cancellationToken: cancellationToken).ConfigureAwait(false);
		try
		{
			return await sessionFactory
				.ConnectAsync(
					profile,
					currentCredential,
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (FtpAuthenticationRequiredException)
		{
			var refreshedCredential = await ResolveCredentialAsync(
				isRetry: true,
				rejectedCredential: currentCredential,
				cancellationToken: cancellationToken).ConfigureAwait(false);
			return await sessionFactory
				.ConnectAsync(
					profile,
					refreshedCredential,
					cancellationToken)
				.ConfigureAwait(false);
		}
	}

	private async ValueTask<FtpCredential> ResolveCredentialAsync(
		bool isRetry,
		FtpCredential? rejectedCredential,
		CancellationToken cancellationToken)
	{
		if (credential is not null
			&& rejectedCredential is null)
		{
			return credential;
		}

		await credentialLock
			.WaitAsync(cancellationToken)
			.ConfigureAwait(false);
		try
		{
			if (rejectedCredential is not null
				&& ReferenceEquals(
					credential,
					rejectedCredential))
			{
				credential = null;
			}

			credential ??= await credentialResolver
				.ResolveAsync(
					new FtpCredentialRequest(
						profile,
						isRetry),
					cancellationToken)
				.ConfigureAwait(false);

			return credential
				?? throw new FtpAuthenticationRequiredException(
					profile.ConnectionId,
					$"Credentials are required for FTP connection '{profile.DisplayName}'.");
		}
		finally
		{
			credentialLock.Release();
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(
			Volatile.Read(ref isDisposed) is not 0,
			this);
	}
}
