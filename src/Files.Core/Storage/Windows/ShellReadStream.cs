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

	private readonly bool _canRead;

	private readonly bool _canWrite;

	private long _length;

	private int _isDisposed;

	public override bool CanRead => _canRead && Volatile.Read(ref _isDisposed) == 0;

	public override bool CanSeek => Volatile.Read(ref _isDisposed) == 0;

	public override bool CanWrite => _canWrite && Volatile.Read(ref _isDisposed) == 0;

	public override long Length
	{
		get
		{
			ThrowIfDisposed();

			return Volatile.Read(ref _length);
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
		: this(scheduler, shellStream, FileAccess.Read)
	{
	}

	public ShellReadStream(IWindowsShellScheduler scheduler, IStream shellStream, FileAccess accessMode)
	{
		ArgumentNullException.ThrowIfNull(scheduler);
		ArgumentNullException.ThrowIfNull(shellStream);
		if (accessMode is not (FileAccess.Read or FileAccess.Write or FileAccess.ReadWrite))
		{
			throw new ArgumentOutOfRangeException(nameof(accessMode));
		}

		_scheduler = scheduler;
		_shellStream = shellStream;
		_canRead = accessMode is FileAccess.Read or FileAccess.ReadWrite;
		_canWrite = accessMode is FileAccess.Write or FileAccess.ReadWrite;

		UpdateLengthOnCurrentSta();
	}

	public override void Flush()
	{
		ThrowIfDisposed();

		if (!_canWrite)
		{
			return;
		}

		_scheduler.InvokeAsync(() =>
		{
			NativeStream.Commit(STGC.STGC_DEFAULT).ThrowOnFailure();

			return true;
		}).GetAwaiter().GetResult();
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

		ThrowIfDisposed();
		EnsureCanRead();

		if (count is 0)
		{
			return Task.FromResult(0);
		}

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

	public override void SetLength(long value)
	{
		ThrowIfDisposed();
		EnsureCanWrite();
		ArgumentOutOfRangeException.ThrowIfNegative(value);

		_scheduler.InvokeAsync(() =>
		{
			NativeStream.SetSize(checked((ulong)value)).ThrowOnFailure();
			Volatile.Write(ref _length, value);

			return true;
		}).GetAwaiter().GetResult();
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
	}

	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(buffer);
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfNegative(count);

		if (buffer.Length - offset < count)
		{
			throw new ArgumentException("The offset and count exceed the buffer length.", nameof(count));
		}

		ThrowIfDisposed();
		EnsureCanWrite();

		if (count is 0)
		{
			return Task.CompletedTask;
		}

		return _scheduler.InvokeAsync(
			() =>
			{
				fixed (byte* source = &buffer[offset])
				{
					uint bytesWritten = 0;
					var result = NativeStream.Write(source, checked((uint)count), &bytesWritten);
					result.ThrowOnFailure();

					if (bytesWritten != count)
					{
						throw new IOException("The Shell stream wrote fewer bytes than requested.");
					}

					UpdateLengthOnCurrentSta();
				}

				return true;
			},
			cancellationToken);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && Interlocked.Exchange(ref _isDisposed, 1) == 0)
		{
			try
			{
				_scheduler.InvokeAsync(() =>
				{
					if (_canWrite)
					{
						NativeStream.Commit(STGC.STGC_DEFAULT).ThrowOnFailure();
					}

					_shellStream = null;

					return true;
				}).GetAwaiter().GetResult();
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

	private void EnsureCanRead()
	{
		if (!_canRead)
		{
			throw new NotSupportedException("The Shell stream was not opened for reading.");
		}
	}

	private void EnsureCanWrite()
	{
		if (!_canWrite)
		{
			throw new NotSupportedException("The Shell stream was not opened for writing.");
		}
	}

	private void UpdateLengthOnCurrentSta()
	{
		STATSTG statistics = default;
		NativeStream.Stat(&statistics, STATFLAG.STATFLAG_NONAME).ThrowOnFailure();
		Volatile.Write(ref _length, checked((long)statistics.cbSize));
	}
}
