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

	private static int IssueSnapshotControl(SafeFileHandle file, Span<byte> output, CancellationToken cancellationToken)
	{
		using var completedEvent = PInvoke.CreateEvent(null, true, false, null);
		if (completedEvent.IsInvalid)
		{
			return Marshal.GetLastPInvokeError();
		}

		IO_STATUS_BLOCK ioStatus = default;
		fixed (byte* outputPointer = output)
		{
			var status = PInvoke.NtFsControlFile(file, completedEvent, 0, 0, ref ioStatus, SnapshotControlCode, 0, 0, ref *outputPointer, checked((uint)output.Length));
			if (status.Value is not StatusPending)
			{
				return status.Value;
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

	private static void CancelAndWait(SafeFileHandle file, SafeFileHandle completedEvent)
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
