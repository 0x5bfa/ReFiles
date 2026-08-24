// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.IO;

namespace Files.Core.Storage.Windows;

internal static unsafe class WindowsPreviousVersionProvider
{
	private const int ErrorTimeout = 1460;
	private const int ProviderTimeoutSeconds = 5;
	private const uint SnapshotControlCode = 0x00144064;
	private const int SnapshotHeaderSize = 12;
	private const int StatusPending = 0x00000103;
	private const uint WaitObject0 = 0;
	private const uint WaitTimeout = 258;
	private const int WaitSliceMilliseconds = 100;
	private const string SnapshotNameFormat = "'@GMT-'yyyy.MM.dd-HH.mm.ss";

	internal static IReadOnlyList<WindowsShellPreviousVersion> Read(string path, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		cancellationToken.ThrowIfCancellationRequested();

		return IsUncPath(path) ? ReadRemoteVersions(path, cancellationToken) : ReadLocalVersions(path, cancellationToken);
	}

	private static IReadOnlyList<WindowsShellPreviousVersion> ReadLocalVersions(string path, CancellationToken cancellationToken)
	{
		try
		{
			var fullPath = Path.GetFullPath(path);
			var root = Path.GetPathRoot(fullPath);
			if (root is null || ReadVolumeName(root) is not { } volumeName)
			{
				return [];
			}

			var relativePath = fullPath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			var options = new EnumerationOptions
			{
				ReturnImmediately = true,
				Rewindable = false,
				Timeout = TimeSpan.FromSeconds(ProviderTimeoutSeconds),
			};
			using var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT DeviceObject, InstallDate, VolumeName FROM Win32_ShadowCopy", options);
			using var snapshots = searcher.Get();
			var versions = new List<WindowsShellPreviousVersion>();
			foreach (ManagementBaseObject snapshot in snapshots)
			{
				using (snapshot)
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (snapshot["VolumeName"] is not string snapshotVolume || !VolumeNamesEqual(volumeName, snapshotVolume)
						|| snapshot["DeviceObject"] is not string deviceObject || snapshot["InstallDate"] is not string installDate)
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
		if (IssueSnapshotControl(file, header, cancellationToken) is not 0)
		{
			return [];
		}

		var snapshotCount = BinaryPrimitives.ReadUInt32LittleEndian(header);
		var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
		if (snapshotCount is 0 || payloadSize is 0 || payloadSize > int.MaxValue - SnapshotHeaderSize)
		{
			return [];
		}

		var output = new byte[checked((int)payloadSize + SnapshotHeaderSize)];
		if (IssueSnapshotControl(file, output, cancellationToken) is not 0)
		{
			return [];
		}

		var returnedCount = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(sizeof(uint))), snapshotCount);
		var returnedSize = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(sizeof(uint) * 2)), payloadSize);
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

		return names;
	}

	private static int IssueSnapshotControl(SafeHandle file, Span<byte> output, CancellationToken cancellationToken)
	{
		using var completedEvent = PInvoke.CreateEvent(null, true, false, null);
		if (completedEvent.IsInvalid)
		{
			return Marshal.GetLastPInvokeError();
		}

		var ioStatus = new IO_STATUS_BLOCK();
		fixed (byte* outputPointer = output)
		{
			var status = PInvoke.NtFsControlFile(file, completedEvent, 0, 0, &ioStatus, SnapshotControlCode, null, 0, outputPointer, checked((uint)output.Length));
			if (status is not StatusPending)
			{
				return status;
			}

			var deadline = Environment.TickCount64 + (ProviderTimeoutSeconds * 1_000L);
			while (true)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					CancelAndWait(file, completedEvent);
					cancellationToken.ThrowIfCancellationRequested();
				}

				var remainingMilliseconds = deadline - Environment.TickCount64;
				if (remainingMilliseconds <= 0)
				{
					CancelAndWait(file, completedEvent);

					return ErrorTimeout;
				}

				var waitResult = (uint)PInvoke.WaitForSingleObjectEx(completedEvent, checked((uint)Math.Min(WaitSliceMilliseconds, remainingMilliseconds)), false);
				if (waitResult is WaitObject0)
				{
					return ioStatus.Status.Value;
				}

				if (waitResult is not WaitTimeout)
				{
					return Marshal.GetLastPInvokeError();
				}
			}
		}
	}

	private static void CancelAndWait(SafeHandle file, SafeHandle completedEvent)
	{
		PInvoke.CancelIoEx(file, null);
		PInvoke.WaitForSingleObjectEx(completedEvent, uint.MaxValue, false);
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

	private static bool VolumeNamesEqual(string left, string right)
	{
		return Path.TrimEndingDirectorySeparator(left).Equals(Path.TrimEndingDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);
	}
}
