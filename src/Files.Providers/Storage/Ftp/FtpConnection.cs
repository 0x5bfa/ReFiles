// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Coordinates credentials and short-lived FTP sessions for one source.
/// </summary>
internal sealed class FtpConnection : IAsyncDisposable
{
	private readonly FtpConnectionProfile _profile;
	private readonly IFtpCredentialResolver _credentialResolver;
	private readonly IFtpSessionFactory _sessionFactory;
	private readonly SemaphoreSlim _credentialLock = new(1, 1);
	private FtpCredential? _credential;
	private int _isDisposed;

	public FtpConnection(FtpConnectionProfile profile, IFtpCredentialResolver credentialResolver, IFtpSessionFactory sessionFactory)
	{
		ArgumentNullException.ThrowIfNull(profile);
		ArgumentNullException.ThrowIfNull(credentialResolver);
		ArgumentNullException.ThrowIfNull(sessionFactory);

		_profile = profile;
		_credentialResolver = credentialResolver;
		_sessionFactory = sessionFactory;
	}

	public async ValueTask<T> ExecuteAsync<T>(Func<IFtpSession, ValueTask<T>> operation, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(operation);

		await using var session = await OpenSessionAsync(cancellationToken).ConfigureAwait(false);

		return await operation(session).ConfigureAwait(false);
	}

	public async ValueTask ExecuteAsync(Func<IFtpSession, ValueTask> operation, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(operation);

		await using var session = await OpenSessionAsync(cancellationToken).ConfigureAwait(false);
		await operation(session).ConfigureAwait(false);
	}

	public ValueTask<Stream> OpenReadAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);

		return OpenStreamAsync(session => session.OpenReadAsync(path, cancellationToken), cancellationToken);
	}

	public ValueTask<Stream> OpenWriteAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);

		return OpenStreamAsync(session => session.OpenWriteAsync(path, cancellationToken), cancellationToken);
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is 0)
		{
			_credential = null;
		}

		GC.SuppressFinalize(this);

		return ValueTask.CompletedTask;
	}

	private async ValueTask<Stream> OpenStreamAsync(Func<IFtpSession, ValueTask<Stream>> openStream, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();

		var session = await OpenSessionAsync(cancellationToken).ConfigureAwait(false);
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
				throw new AggregateException("FTP stream opening and session cleanup both failed.", openError, cleanupError);
			}

			throw;
		}
	}

	private async ValueTask<IFtpSession> OpenSessionAsync(CancellationToken cancellationToken)
	{
		var currentCredential = await ResolveCredentialAsync(isRetry: false, rejectedCredential: null, cancellationToken: cancellationToken).ConfigureAwait(false);
		try
		{
			return await _sessionFactory.ConnectAsync(_profile, currentCredential, cancellationToken).ConfigureAwait(false);
		}
		catch (FtpAuthenticationRequiredException)
		{
			var refreshedCredential = await ResolveCredentialAsync(isRetry: true, rejectedCredential: currentCredential, cancellationToken: cancellationToken).ConfigureAwait(false);

			return await _sessionFactory.ConnectAsync(_profile, refreshedCredential, cancellationToken).ConfigureAwait(false);
		}
	}

	private async ValueTask<FtpCredential> ResolveCredentialAsync(bool isRetry, FtpCredential? rejectedCredential, CancellationToken cancellationToken)
	{
		if (_credential is not null && rejectedCredential is null)
		{
			return _credential;
		}

		await _credentialLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (rejectedCredential is not null && ReferenceEquals(_credential, rejectedCredential))
			{
				_credential = null;
			}

			_credential ??= await _credentialResolver.ResolveAsync(new FtpCredentialRequest(_profile, isRetry), cancellationToken).ConfigureAwait(false);

			return _credential
				?? throw new FtpAuthenticationRequiredException(_profile.ConnectionId, $"Credentials are required for FTP connection '{_profile.DisplayName}'.");
		}
		finally
		{
			_credentialLock.Release();
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

	}
}
