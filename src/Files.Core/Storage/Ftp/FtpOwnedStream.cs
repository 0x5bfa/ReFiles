// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Keeps the FTP control session alive until its data stream is closed.
/// </summary>
internal sealed class FtpOwnedStream : Stream
{
	private readonly Stream innerStream;
	private readonly IFtpSession session;
	private int isDisposed;

	public FtpOwnedStream(Stream innerStream, IFtpSession session)
	{
		ArgumentNullException.ThrowIfNull(innerStream);
		ArgumentNullException.ThrowIfNull(session);
		this.innerStream = innerStream;
		this.session = session;
	}

	public override bool CanRead => innerStream.CanRead;

	public override bool CanSeek => innerStream.CanSeek;

	public override bool CanWrite => innerStream.CanWrite;

	public override bool CanTimeout => innerStream.CanTimeout;

	public override long Length => innerStream.Length;

	public override long Position
	{
		get => innerStream.Position;
		set => innerStream.Position = value;
	}

	public override int ReadTimeout
	{
		get => innerStream.ReadTimeout;
		set => innerStream.ReadTimeout = value;
	}

	public override int WriteTimeout
	{
		get => innerStream.WriteTimeout;
		set => innerStream.WriteTimeout = value;
	}

	public override void Flush() => innerStream.Flush();

	public override Task FlushAsync(CancellationToken cancellationToken)
		=> innerStream.FlushAsync(cancellationToken);

	public override int Read(byte[] buffer, int offset, int count)
		=> innerStream.Read(buffer, offset, count);

	public override int Read(Span<byte> buffer)
		=> innerStream.Read(buffer);

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		=> innerStream.ReadAsync(buffer, cancellationToken);

	public override long Seek(long offset, SeekOrigin origin)
		=> innerStream.Seek(offset, origin);

	public override void SetLength(long value)
		=> innerStream.SetLength(value);

	public override void Write(byte[] buffer, int offset, int count)
		=> innerStream.Write(buffer, offset, count);

	public override void Write(ReadOnlySpan<byte> buffer)
		=> innerStream.Write(buffer);

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		=> innerStream.WriteAsync(buffer, cancellationToken);

	public override async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
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
		if (!disposing
			|| Interlocked.Exchange(ref isDisposed, 1) is not 0)
		{
			base.Dispose(disposing);
			return;
		}

		var errors = new List<Exception>();
		try
		{
			innerStream.Dispose();
		}
		catch (Exception error)
		{
			errors.Add(error);
		}

		TryWait(session.CompleteTransferAsync(CancellationToken.None), errors);
		TryWait(session.DisposeAsync(), errors);
		base.Dispose(disposing);
		GC.SuppressFinalize(this);
		ThrowDisposalErrors(errors);
	}

	private async ValueTask TryDisposeStreamAsync(ICollection<Exception> errors)
	{
		try
		{
			await innerStream.DisposeAsync().ConfigureAwait(false);
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
			await session
				.CompleteTransferAsync(CancellationToken.None)
				.ConfigureAwait(false);
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
			await session.DisposeAsync().ConfigureAwait(false);
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
