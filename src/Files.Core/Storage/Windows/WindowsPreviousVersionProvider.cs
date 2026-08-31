// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.IO;

namespace Files.Core.Storage.Windows;

internal static unsafe class WindowsPreviousVersionProvider
{
	private const int ErrorTimeout = 1460;
	private const int MinimumVolumePathCapacity = 261;
	private const int ProviderTimeoutSeconds = 5;
	private const uint SnapshotControlCode = 0x00144064;
	private const int SnapshotHeaderSize = 12;
	private const int StatusPending = 0x00000103;
	private const uint WaitObject0 = 0;
	private const uint WaitTimeout = 258;
	private const int CancelCompletionWaitMilliseconds = 1_000;
	private const int WaitSliceMilliseconds = 100;
	private const uint MaximumSnapshotCount = 4_096;
	private const uint MaximumSnapshotPayloadSize = 1_048_576;
	private const string SnapshotNameFormat = "'@GMT-'yyyy.MM.dd-HH.mm.ss";

	internal static IReadOnlyList<WindowsShellPreviousVersion> Read(string path, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		cancellationToken.ThrowIfCancellationRequested();

		return IsUncPath(path) ? ReadRemoteVersions(path, cancellationToken) : ReadLocalVersions(path, cancellationToken);
	}

	private static IReadOnlyList<WindowsShellPreviousVersion> ReadLocalVersions(string path, CancellationToken cancellationToken)
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
		{
			return [];
		}

		try
		{
			var fullPath = Path.GetFullPath(path);
			var volumePath = ReadVolumePath(fullPath);
			var volumeRoot = volumePath is null ? null : Path.TrimEndingDirectorySeparator(volumePath);
			if (volumePath is null || volumeRoot is null || (!fullPath.Equals(volumeRoot, StringComparison.OrdinalIgnoreCase) && !fullPath.StartsWith(volumePath, StringComparison.OrdinalIgnoreCase))
				|| ReadVolumeName(volumePath) is not { } volumeName)
			{
				return [];
			}

			var relativePath = fullPath.Equals(volumeRoot, StringComparison.OrdinalIgnoreCase) ? string.Empty : fullPath[volumePath.Length..];
			var options = new System.Management.EnumerationOptions
			{
				ReturnImmediately = true,
				Rewindable = false,
				Timeout = TimeSpan.FromSeconds(ProviderTimeoutSeconds),
			};
			var query = CreateShadowCopyQuery(volumeName);
			using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\cimv2"), new ObjectQuery(query), options);
			using var snapshots = searcher.Get();
			var versions = new List<WindowsShellPreviousVersion>();
			foreach (ManagementBaseObject snapshot in snapshots)
			{
				using (snapshot)
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (snapshot["DeviceObject"] is not string deviceObject || snapshot["InstallDate"] is not string installDate)
					{
						continue;
					}

					var sourcePath = string.IsNullOrEmpty(relativePath) ? deviceObject + Path.DirectorySeparatorChar : Path.Combine(deviceObject, relativePath);
					if ((File.Exists(sourcePath) || Directory.Exists(sourcePath)) && TryReadManagementDate(installDate, out var timestamp))
					{
						versions.Add(new(GetDisplayName(path), sourcePath, timestamp));
					}
				}
			}

			return versions.OrderByDescending(static version => version.DateModified).ToArray();
		}
		catch (Exception exception) when (exception is ManagementException or COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			return [];
		}
	}

	private static IReadOnlyList<WindowsShellPreviousVersion> ReadRemoteVersions(string path, CancellationToken cancellationToken)
	{
		var snapshotNames = ReadSnapshotNames(path, cancellationToken);
		if (snapshotNames.Count is 0)
		{
			return [];
		}

		var name = GetDisplayName(path);
		var versions = new List<WindowsShellPreviousVersion>(snapshotNames.Count);
		foreach (var snapshotName in snapshotNames)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (DateTimeOffset.TryParseExact(snapshotName, SnapshotNameFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
				out var timestamp))
			{
				var sourcePath = BuildRemoteSnapshotItemPath(path, snapshotName);
				if (File.Exists(sourcePath) || Directory.Exists(sourcePath))
				{
					versions.Add(new(name, sourcePath, timestamp.ToLocalTime()));
				}
			}
		}

		return versions.OrderByDescending(static version => version.DateModified).ToArray();
	}

	private static IReadOnlyList<string> ReadSnapshotNames(string path, CancellationToken cancellationToken)
	{
		using var file = PInvoke.CreateFile(
			path,
			(uint)FILE_ACCESS_RIGHTS.FILE_READ_DATA,
			FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
			null,
			FILE_CREATION_DISPOSITION.OPEN_EXISTING,
			FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
			null);
		if (file.IsInvalid)
		{
			return [];
		}

		Span<byte> header = stackalloc byte[16];
		var headerResult = IssueSnapshotControl(file, header, cancellationToken);
		if (headerResult.Status is not 0 || headerResult.Information < (nuint)SnapshotHeaderSize || headerResult.Information > (nuint)header.Length)
		{
			return [];
		}

		var snapshotCount = BinaryPrimitives.ReadUInt32LittleEndian(header);
		var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
		if (snapshotCount is 0 or > MaximumSnapshotCount || payloadSize is 0 or > MaximumSnapshotPayloadSize || payloadSize > int.MaxValue - SnapshotHeaderSize)
		{
			return [];
		}

		var output = new byte[checked((int)payloadSize + SnapshotHeaderSize)];
		var snapshotResult = IssueSnapshotControl(file, output, cancellationToken);
		if (snapshotResult.Status is not 0 || snapshotResult.Information < (nuint)SnapshotHeaderSize || snapshotResult.Information > (nuint)output.Length)
		{
			return [];
		}

		var returnedCount = BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(sizeof(uint)));
		var returnedSize = BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(sizeof(uint) * 2));
		if (returnedCount > snapshotCount || returnedCount > MaximumSnapshotCount || returnedSize == 0 || returnedSize > payloadSize || (returnedSize & 1) != 0 ||
			returnedCount > returnedSize / sizeof(char))
		{
			return [];
		}

		var requiredResponseSize = (nuint)SnapshotHeaderSize + returnedSize;
		if (requiredResponseSize > snapshotResult.Information)
		{
			return [];
		}

		var characters = MemoryMarshal.Cast<byte, char>(output.AsSpan(SnapshotHeaderSize, checked((int)returnedSize)));
		var names = new List<string>(checked((int)returnedCount));
		while (names.Count < returnedCount && !characters.IsEmpty)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var terminator = characters.IndexOf('\0');
			if (terminator <= 0)
			{
				break;
			}

			names.Add(new(characters[..terminator]));
			characters = characters[(terminator + 1)..];
		}

		return names.Count == returnedCount ? names : [];
	}

	private static SnapshotControlResult IssueSnapshotControl(SafeHandle file, Span<byte> output, CancellationToken cancellationToken)
	{
		var completedEvent = PInvoke.CreateEvent(null, true, false, null);
		if (completedEvent.IsInvalid)
		{
			var error = Marshal.GetLastPInvokeError();
			completedEvent.Dispose();

			return new(error, 0);
		}

		var request = new PendingSnapshotControl(completedEvent, output.Length);
		try
		{
			var status = PInvoke.NtFsControlFile(file, request.CompletedEvent, 0, 0, request.IoStatus, SnapshotControlCode, null, 0, request.OutputBuffer, checked((uint)output.Length));
			if (status is not StatusPending)
			{
				var result = new SnapshotControlResult(status, request.IoStatus->Information);
				if (status is 0)
				{
					CopySnapshotControlOutput(request, output, result.Information);
				}

				return result;
			}

			var deadline = Environment.TickCount64 + (ProviderTimeoutSeconds * 1_000L);
			while (true)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					if (!CancelAndWait(file, request.CompletedEvent))
					{
						request.DisposeWhenComplete();
						request = null!;
					}

					cancellationToken.ThrowIfCancellationRequested();
				}

				var remainingMilliseconds = deadline - Environment.TickCount64;
				if (remainingMilliseconds <= 0)
				{
					if (!CancelAndWait(file, request.CompletedEvent))
					{
						request.DisposeWhenComplete();
						request = null!;
					}

					return new(ErrorTimeout, 0);
				}

				var waitResult = (uint)PInvoke.WaitForSingleObjectEx(request.CompletedEvent, checked((uint)Math.Min(WaitSliceMilliseconds, remainingMilliseconds)), false);
				if (waitResult is WaitObject0)
				{
					var completedStatus = request.IoStatus->Status.Value;
					var result = new SnapshotControlResult(completedStatus, request.IoStatus->Information);
					if (completedStatus is 0)
					{
						CopySnapshotControlOutput(request, output, result.Information);
					}

					return result;
				}

				if (waitResult is not WaitTimeout)
				{
					var error = Marshal.GetLastPInvokeError();
					if (!CancelAndWait(file, request.CompletedEvent))
					{
						request.DisposeWhenComplete();
						request = null!;
					}

					return new(error, 0);
				}
			}
		}
		finally
		{
			request?.Dispose();
		}
	}

	private static void CopySnapshotControlOutput(PendingSnapshotControl request, Span<byte> output, nuint information)
	{
		if (information <= (nuint)output.Length)
		{
			request.CopyTo(output, information);
		}
	}

	private static bool CancelAndWait(SafeHandle file, SafeHandle completedEvent)
	{
		PInvoke.CancelIoEx(file, null);

		return (uint)PInvoke.WaitForSingleObjectEx(completedEvent, CancelCompletionWaitMilliseconds, false) is WaitObject0;
	}

	private readonly record struct SnapshotControlResult(int Status, nuint Information);

	private sealed unsafe class PendingSnapshotControl : IDisposable
	{
		private readonly Lock _syncRoot = new();
		private SafeHandle? _completedEvent;
		private EventWaitHandle? _completionWaitHandle;
		private RegisteredWaitHandle? _completionRegistration;
		private void* _ioStatus;
		private void* _outputBuffer;
		private readonly int _outputLength;
		private int _isDisposed;

		public SafeHandle CompletedEvent => _completedEvent ?? throw new ObjectDisposedException(nameof(PendingSnapshotControl));

		public IO_STATUS_BLOCK* IoStatus => (IO_STATUS_BLOCK*)_ioStatus;

		public void* OutputBuffer => _outputBuffer;

		public PendingSnapshotControl(SafeHandle completedEvent, int outputLength)
		{
			ArgumentNullException.ThrowIfNull(completedEvent);
			ArgumentOutOfRangeException.ThrowIfNegative(outputLength);

			_completedEvent = completedEvent;
			_outputLength = outputLength;
			try
			{
				_completionWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset)
				{
					SafeWaitHandle = new SafeWaitHandle(completedEvent.DangerousGetHandle(), ownsHandle: false),
				};
				_ioStatus = Allocate((nuint)sizeof(IO_STATUS_BLOCK));
				_outputBuffer = Allocate((nuint)outputLength);
				*IoStatus = default;
			}
			catch
			{
				Dispose();

				throw;
			}
		}

		public void CopyTo(Span<byte> destination, nuint byteCount)
		{
			if (destination.Length != _outputLength || byteCount > (nuint)_outputLength)
			{
				throw new ArgumentException("The snapshot-control output length changed.", nameof(destination));
			}

			new ReadOnlySpan<byte>(_outputBuffer, checked((int)byteCount)).CopyTo(destination);
		}

		public void DisposeWhenComplete()
		{
			var disposeAfterWait = false;
			lock (_syncRoot)
			{
				if (_completionWaitHandle is null || _isDisposed != 0)
				{
					throw new ObjectDisposedException(nameof(PendingSnapshotControl));
				}

				try
				{
					_completionRegistration = ThreadPool.RegisterWaitForSingleObject(_completionWaitHandle, static (state, _) => ((PendingSnapshotControl)state!).DisposeFromCompletion(), this,
						Timeout.Infinite, executeOnlyOnce: true);
				}
				catch
				{
					if ((uint)PInvoke.WaitForSingleObjectEx(CompletedEvent, uint.MaxValue, false) is not WaitObject0)
					{
						Environment.FailFast("A pending snapshot-control request could not be retained safely.");
					}

					disposeAfterWait = true;
				}
			}

			if (disposeAfterWait)
			{
				DisposeCore();
			}
		}

		public void Dispose()
		{
			DisposeCore();
		}

		private static void* Allocate(nuint byteCount)
		{
			var memory = NativeMemory.AllocZeroed(byteCount);

			return memory is null ? throw new OutOfMemoryException() : memory;
		}

		private void DisposeFromCompletion()
		{
			lock (_syncRoot)
			{
				_completionRegistration = null;
			}

			DisposeCore();
		}

		private void DisposeCore()
		{
			RegisteredWaitHandle? completionRegistration;
			EventWaitHandle? completionWaitHandle;
			SafeHandle? completedEvent;
			void* ioStatus;
			void* outputBuffer;
			lock (_syncRoot)
			{
				if (_isDisposed != 0)
				{
					return;
				}

				_isDisposed = 1;
				completionRegistration = _completionRegistration;
				_completionRegistration = null;
				completionWaitHandle = _completionWaitHandle;
				_completionWaitHandle = null;
				completedEvent = _completedEvent;
				_completedEvent = null;
				ioStatus = _ioStatus;
				_ioStatus = null;
				outputBuffer = _outputBuffer;
				_outputBuffer = null;
			}

			completionRegistration?.Unregister(null);
			completionWaitHandle?.Dispose();
			completedEvent?.Dispose();
			NativeMemory.Free(ioStatus);
			NativeMemory.Free(outputBuffer);
		}
	}

	private static string BuildRemoteSnapshotItemPath(string sourcePath, string snapshotName)
	{
		var root = Path.GetPathRoot(sourcePath) ?? string.Empty;
		var relativePath = sourcePath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var snapshotRoot = Path.Combine(root, snapshotName);

		return string.IsNullOrEmpty(relativePath) ? snapshotRoot : Path.Combine(snapshotRoot, relativePath);
	}

	private static string GetDisplayName(string path)
	{
		var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

		return string.IsNullOrEmpty(name) ? path : name;
	}

	private static bool IsUncPath(string path)
	{
		return path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal);
	}

	private static string CreateShadowCopyQuery(string volumeName)
	{
		var escapedVolumeName = volumeName.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("'", @"\'", StringComparison.Ordinal);

		return $"SELECT DeviceObject, InstallDate FROM Win32_ShadowCopy WHERE VolumeName = '{escapedVolumeName}'";
	}

	private static string? ReadVolumePath(string fullPath)
	{
		var volumePath = new char[Math.Max(MinimumVolumePathCapacity, fullPath.Length + 1)];
		if (!PInvoke.GetVolumePathName(fullPath, volumePath))
		{
			return null;
		}

		var terminator = volumePath.AsSpan().IndexOf('\0');

		return volumePath.AsSpan(0, terminator < 0 ? volumePath.Length : terminator).ToString();
	}

	private static string? ReadVolumeName(string root)
	{
		Span<char> volumeName = stackalloc char[64];
		if (!PInvoke.GetVolumeNameForVolumeMountPoint(root, volumeName))
		{
			return null;
		}

		var terminator = volumeName.IndexOf('\0');

		return volumeName[..(terminator < 0 ? volumeName.Length : terminator)].ToString();
	}

	private static bool TryReadManagementDate(string value, out DateTimeOffset timestamp)
	{
		try
		{
			timestamp = new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(value));

			return true;
		}
		catch (ArgumentException)
		{
			timestamp = default;

			return false;
		}
	}
}
