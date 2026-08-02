// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Keeps the FTP control session alive until its data stream is closed.
/// </summary>
internal sealed class FtpOwnedStream : Stream
{
	private readonly Stream _innerStream;

	private readonly IFtpSession _session;

	private int _isDisposed;

	public override bool CanRead => _innerStream.CanRead;

	public override bool CanSeek => _innerStream.CanSeek;

	public override bool CanWrite => _innerStream.CanWrite;

	public override bool CanTimeout => _innerStream.CanTimeout;

	public override long Length => _innerStream.Length;

	public override long Position
	{
		get => _innerStream.Position;
		set => _innerStream.Position = value;
	}

	public override int ReadTimeout
	{
		get => _innerStream.ReadTimeout;
		set => _innerStream.ReadTimeout = value;
	}

	public override int WriteTimeout
	{
		get => _innerStream.WriteTimeout;
		set => _innerStream.WriteTimeout = value;
	}

	public FtpOwnedStream(Stream innerStream, IFtpSession session)
	{
		ArgumentNullException.ThrowIfNull(innerStream);
		ArgumentNullException.ThrowIfNull(session);

		_innerStream = innerStream;
		_session = session;
	}

	public override void Flush() => _innerStream.Flush();

	public override Task FlushAsync(CancellationToken cancellationToken)
		=> _innerStream.FlushAsync(cancellationToken);

	public override int Read(byte[] buffer, int offset, int count)
		=> _innerStream.Read(buffer, offset, count);

	public override int Read(Span<byte> buffer)
		=> _innerStream.Read(buffer);

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		=> _innerStream.ReadAsync(buffer, cancellationToken);

	public override long Seek(long offset, SeekOrigin origin)
		=> _innerStream.Seek(offset, origin);

	public override void SetLength(long value)
		=> _innerStream.SetLength(value);

	public override void Write(byte[] buffer, int offset, int count)
		=> _innerStream.Write(buffer, offset, count);

	public override void Write(ReadOnlySpan<byte> buffer)
		=> _innerStream.Write(buffer);

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		=> _innerStream.WriteAsync(buffer, cancellationToken);

	public override async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		var errors = new List<Exception>();
		await TryDisposeStreamAsync(errors).ConfigureAwait(false);
		await TryCompleteTransferAsync(errors).ConfigureAwait(false);
		await TryDisposeSessionAsync(errors).ConfigureAwait(false);
		GC.SuppressFinalize(this);
		ThrowDisposalErrors(errors);
	}

	protected override void Dispose(bool disposing)
	{
		if (!disposing || Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			base.Dispose(disposing);

			return;
		}

		var errors = new List<Exception>();
		try
		{
			_innerStream.Dispose();
		}
		catch (Exception error)
		{
			errors.Add(error);
		}

		TryWait(_session.CompleteTransferAsync(CancellationToken.None), errors);
		TryWait(_session.DisposeAsync(), errors);
		base.Dispose(disposing);
		GC.SuppressFinalize(this);
		ThrowDisposalErrors(errors);
	}

	private async ValueTask TryDisposeStreamAsync(ICollection<Exception> errors)
	{
		try
		{
			await _innerStream.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
	}

	private async ValueTask TryCompleteTransferAsync(ICollection<Exception> errors)
	{
		try
		{
			await _session.CompleteTransferAsync(CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
	}

	private async ValueTask TryDisposeSessionAsync(ICollection<Exception> errors)
	{
		try
		{
			await _session.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
	}

	private static void TryWait(ValueTask operation, ICollection<Exception> errors)
	{
		try
		{
			operation.AsTask().GetAwaiter().GetResult();
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
	}

	private static void ThrowDisposalErrors(IReadOnlyCollection<Exception> errors)
	{
		if (errors.Count is 1)
		{
			throw errors.Single();
		}

		if (errors.Count > 1)
		{
			throw new AggregateException("The FTP data transfer did not close cleanly.", errors);
		}
	}
}
