// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Capabilities.Content;

internal sealed class ReadOnlyContentStream(Stream inner) : Stream
{
	private int _isDisposed;

	public override bool CanRead => Volatile.Read(ref _isDisposed) == 0 && inner.CanRead;
	public override bool CanSeek => Volatile.Read(ref _isDisposed) == 0 && inner.CanSeek;
	public override bool CanWrite => false;
	public override bool CanTimeout => inner.CanTimeout;
	public override long Length => inner.Length;

	public override long Position
	{
		get => inner.Position;
		set => inner.Position = value;
	}

	public override int ReadTimeout
	{
		get => inner.ReadTimeout;
		set => inner.ReadTimeout = value;
	}

	public override void Flush()
	{
		inner.Flush();
	}

	public override Task FlushAsync(CancellationToken cancellationToken)
	{
		return inner.FlushAsync(cancellationToken);
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		return inner.Read(buffer, offset, count);
	}

	public override int Read(Span<byte> buffer)
	{
		return inner.Read(buffer);
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		return inner.ReadAsync(buffer, offset, count, cancellationToken);
	}

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		return inner.ReadAsync(buffer, cancellationToken);
	}

	public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
	{
		return inner.CopyToAsync(destination, bufferSize, cancellationToken);
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		return inner.Seek(offset, origin);
	}

	public override void SetLength(long value)
	{
		throw new NotSupportedException("The content stream is read-only.");
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException("The content stream is read-only.");
	}

	public override async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
		{
			return;
		}

		await inner.DisposeAsync().ConfigureAwait(false);
		GC.SuppressFinalize(this);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && Interlocked.Exchange(ref _isDisposed, 1) == 0)
		{
			inner.Dispose();
		}

		base.Dispose(disposing);
	}
}
