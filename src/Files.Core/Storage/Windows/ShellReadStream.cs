// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Windows.Win32.System.Com;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Keeps a virtual Shell stream private and routes every COM call through its STA lane.
/// </summary>
internal sealed unsafe class ShellReadStream : Stream
{
	private readonly IWindowsShellScheduler _scheduler;

	private IStream? _shellStream;

	private readonly long _length;

	private int _isDisposed;

	public override bool CanRead => Volatile.Read(ref _isDisposed) == 0;

	public override bool CanSeek => Volatile.Read(ref _isDisposed) == 0;

	public override bool CanWrite => false;

	public override long Length
	{
		get
		{
			ThrowIfDisposed();

			return _length;
		}
	}

	public override long Position
	{
		get => Seek(0, SeekOrigin.Current);
		set => Seek(value, SeekOrigin.Begin);
	}

	private IStream NativeStream
	{
		get => _shellStream ?? throw new ObjectDisposedException(nameof(ShellReadStream));
	}

	public ShellReadStream(IWindowsShellScheduler scheduler, IStream shellStream)
	{
		ArgumentNullException.ThrowIfNull(scheduler);
		ArgumentNullException.ThrowIfNull(shellStream);

		_scheduler = scheduler;
		_shellStream = shellStream;

		STATSTG statistics = default;
		var result = shellStream.Stat(&statistics, STATFLAG.STATFLAG_NONAME);
		result.ThrowOnFailure();
		_length = checked((long)statistics.cbSize);
	}

	public override void Flush()
	{
		ThrowIfDisposed();

	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(buffer);
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfNegative(count);

		if (buffer.Length - offset < count)
		{
			throw new ArgumentException("The offset and count exceed the buffer length.", nameof(count));
		}

		if (count is 0)
		{
			return Task.FromResult(0);
		}

		ThrowIfDisposed();

		return _scheduler.InvokeAsync(
			() =>
			{
				fixed (byte* destination = &buffer[offset])
				{
					uint bytesRead = 0;
					var result = NativeStream.Read(destination, checked((uint)count), &bytesRead);
					result.ThrowOnFailure();

					return checked((int)bytesRead);
				}
			},
			cancellationToken);
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		ThrowIfDisposed();

		return _scheduler.InvokeAsync(() => { ulong position = 0; var result = NativeStream.Seek(offset, origin, &position); result.ThrowOnFailure(); return checked((long)position); }).GetAwaiter().GetResult();
	}

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	protected override void Dispose(bool disposing)
	{
		if (disposing && Interlocked.Exchange(ref _isDisposed, 1) == 0)
		{
			try
			{
				_scheduler.InvokeAsync(() => {_shellStream = null; return true;}).GetAwaiter().GetResult();
			}
			finally
			{
				base.Dispose(disposing);
			}

			return;
		}

		base.Dispose(disposing);
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
	}
}
